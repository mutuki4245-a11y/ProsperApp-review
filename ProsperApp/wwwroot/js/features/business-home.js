(() => {
    const config = window.prosperBusinessHome ?? {};
    const form = document.querySelector('[data-business-karaoke-form]');
    const list = document.querySelector('[data-business-slip-list]');
    if (!form || !list) {
        return;
    }

    const saveStatus = window.TerminalSaveStatus;
    const status = form.querySelector('[data-business-karaoke-status]');
    const revealButton = document.querySelector('[data-slip-amount-reveal]');
    const businessDateDisplay = document.querySelector('[data-business-date-display]');
    const createDateInput = document.querySelector('[data-business-create-date-input]');
    const createDayIdInput = document.querySelector('[data-business-create-day-id-input]');
    const createDateDisplay = document.querySelector('[data-business-create-date-display]');
    const openSlipCount = document.querySelector('[data-business-open-slip-count]');
    const checkedOutSlipCount = document.querySelector('[data-business-checked-out-slip-count]');
    const estimatedSalesAmount = document.querySelector('[data-business-estimated-sales-amount]');
    const businessSlipsUrl = config.businessSlipsUrl || '';
    const flushBusinessHomeChangesUrl = config.flushBusinessHomeChangesUrl || '';
    const refreshIntervalMs = 10000;
    const accountingUnit = 240;
    let slips = [];
    let serverSnapshot = null;
    let snapshotRevision = -1;
    const pendingOperations = new Map();
    const checkoutLockSlipIds = new Set();
    const expandedSlipIds = new Set();
    const expandedOrderGroupKeys = new Set();
    let hasLoaded = false;
    let refreshPromise = null;
    let isSaving = false;
    let flushPromise = null;
    let pendingFlushBatch = null;
    let flushTimer = null;
    let navigationInFlight = false;

    const formatYen = window.MoneyText.yen;
    const formatSignedYen = (value) => {
        const amount = Math.round(Number(value) || 0);
        return amount < 0 ? `-${formatYen(Math.abs(amount))}` : formatYen(amount);
    };
    const toQuantity = (value) => Math.max(0, Math.trunc(Number(value) || 0));
    const setText = (element, text) => {
        if (element && element.textContent !== String(text)) {
            element.textContent = String(text);
        }
    };

    const operationTimeoutMs = 10000;
    const pendingForSlip = (slipId) => Array.from(pendingOperations.values())
        .filter((operation) => String(operation.slipId) === String(slipId));

    const hasPendingOperations = () => pendingOperations.size > 0;

    const showOperationNotice = (message) => {
        let notice = document.querySelector('[data-business-operation-notice]');
        if (!notice) {
            notice = buildElement('div', 'alert alert-warning sales-operation-notice');
            notice.dataset.businessOperationNotice = '';
            notice.setAttribute('role', 'status');
            list.parentElement?.insertBefore(notice, list);
        }
        notice.textContent = message;
        notice.hidden = false;
        window.setTimeout(() => {
            if (notice?.textContent === message) {
                notice.hidden = true;
            }
        }, 7000);
    };

    const cloneSnapshot = (snapshot) => {
        if (!snapshot) return null;
        return JSON.parse(JSON.stringify(snapshot));
    };

    const refreshSlipSummary = (slip) => {
        const customers = Array.isArray(slip.customers) ? slip.customers.filter((item) => item.status === 'active') : [];
        const nominations = Array.isArray(slip.nominations) ? slip.nominations.filter((item) => item.status !== 'cancelled') : [];
        const orders = Array.isArray(slip.orders) ? slip.orders.filter((item) => item.status === 'active') : [];
        const adjustments = Array.isArray(slip.adjustments) ? slip.adjustments.filter((item) => item.status === 'active') : [];
        const pricingLines = Array.isArray(slip.pricingLines) ? slip.pricingLines.filter((item) => item.status === 'active') : null;
        const names = customers.map((item) => item.displayName || `客${item.lineNo || ''}`).filter(Boolean);
        const castNames = [];
        nominations.forEach((item) => {
            if (item.displayName && !castNames.includes(item.displayName)) castNames.push(item.displayName);
        });
        slip.customerCount = customers.filter((item) => item.status === 'active').length;
        slip.customerNames = names.join('、') || '客名なし';
        slip.castNames = castNames.join('、') || '指名なし';
        slip.orderCount = orders.length;
        // 営業中のセット・延長料金は、明細ではシステム商品として見せますが、
        // 金額の正本は直前スナップショットの pricingLines です。
        slip.orderSubtotalAmount = orders
            .filter((item) => item.isDynamicPricing !== true)
            .reduce((sum, item) => sum + (Number(item.amount) || 0), 0);
        // 時間料金そのものはサーバー側の料金計算モジュールだけが決めます。
        // 楽観表示では直前スナップショットの料金案を保ったまま、通常注文だけを反映します。
        if (pricingLines) {
            slip.pricingSubtotalAmount = pricingLines.reduce((sum, item) => sum + (Number(item.amount) || 0), 0);
        }
        slip.adjustmentAmount = adjustments.reduce((sum, item) => sum + (Number(item.amount) || 0), 0);
        slip.karaokeQuantity = orders
            .filter((item) => item.itemType === 'karaoke')
            .reduce((sum, item) => sum + (Number(item.quantity) || 0), 0);
        const billableSubtotal = slip.orderSubtotalAmount + (Number(slip.pricingSubtotalAmount) || 0);
        slip.accountingAmount = Math.max(0, billableSubtotal + Math.round(billableSubtotal * 0.20) + slip.adjustmentAmount);
    };

    const projectOperation = (snapshot, operation) => {
        const slip = snapshot?.slips?.find((item) => String(item.id) === String(operation.slipId));
        if (!slip) return;
        const payload = operation.payload || {};
        const temporaryId = `temporary:${operation.operationId}`;
        const now = new Date().toISOString();
        slip.customers ??= [];
        slip.nominations ??= [];
        slip.orders ??= [];
        slip.adjustments ??= [];

        if (operation.operationType === 'add_customer') {
            const lineNo = Math.max(0, ...slip.customers.map((item) => Number(item.lineNo) || 0)) + 1;
            const label = String(payload.customer_label || '').trim();
            slip.customers.push({
                id: temporaryId,
                lineNo,
                customerLabel: label || null,
                displayName: label || `客${lineNo}`,
                enteredTime: payload.entered_time || '',
                enteredAt: now,
                leftAt: null,
                leftTime: null,
                status: 'active',
                pending: true
            });
        } else if (operation.operationType === 'update_customer') {
            const customer = slip.customers.find((item) => String(item.id) === String(payload.slip_customer_id));
            if (customer) {
                const label = String(payload.customer_label || '').trim();
                customer.customerLabel = label || null;
                customer.displayName = label || `客${customer.lineNo || ''}`;
                customer.pending = true;
            }
        } else if (operation.operationType === 'leave_customer') {
            const customer = slip.customers.find((item) => String(item.id) === String(payload.slip_customer_id));
            if (customer) {
                customer.leftTime = payload.left_time || '';
                customer.leftAt = now;
                customer.status = 'left';
                customer.pending = true;
            }
        } else if (operation.operationType === 'add_nomination') {
            const displayName = payload.cast_display_name || 'キャスト';
            const price = Number(payload.nomination_price) || 0;
            slip.nominations.push({
                id: temporaryId,
                castId: payload.cast_id,
                displayName,
                nominationKind: payload.nomination_kind,
                nominationDisplayName: payload.nomination_display_name || payload.nomination_kind || '指名',
                nominationPrice: price,
                startedAt: now,
                startedTime: '',
                status: 'active',
                pending: true
            });
            slip.orders.push({
                id: `${temporaryId}:fee`,
                lineNo: Math.max(0, ...slip.orders.map((item) => Number(item.lineNo) || 0)) + 1,
                itemName: '指名料金',
                itemType: 'nomination_fee',
                quantity: 1,
                unitPrice: price,
                amount: price,
                orderedAt: now,
                orderedTime: '',
                status: 'active',
                sourceType: 'nomination_fee',
                sourceId: temporaryId,
                pending: true
            });
        } else if (operation.operationType === 'cancel_nomination') {
            const nomination = slip.nominations.find((item) => String(item.id) === String(payload.slip_cast_id));
            if (nomination) {
                nomination.status = 'cancelled';
                nomination.pending = true;
            }
            slip.orders.forEach((order) => {
                if (order.itemType === 'nomination_fee' && String(order.sourceId) === String(payload.slip_cast_id)) {
                    order.status = 'voided';
                    order.pending = true;
                }
            });
        } else if (operation.operationType === 'add_adjustment') {
            const amount = Number(payload.amount) || 0;
            slip.adjustments.push({
                id: temporaryId,
                lineNo: Math.max(0, ...slip.adjustments.map((item) => Number(item.lineNo) || 0)) + 1,
                lineName: String(payload.line_name || '').trim(),
                amount,
                createdAt: now,
                createdTime: '',
                status: 'active',
                pending: true
            });
        } else if (operation.operationType === 'void_adjustment') {
            const adjustment = slip.adjustments.find((item) => String(item.id) === String(payload.charge_line_id));
            if (adjustment) {
                adjustment.status = 'voided';
                adjustment.pending = true;
            }
        } else if (operation.operationType === 'add_order') {
            const quantity = Math.max(1, Math.trunc(Number(payload.quantity) || 0));
            const unitPrice = Number(payload.unit_price) || 0;
            slip.orders.push({
                id: temporaryId,
                lineNo: Math.max(0, ...slip.orders.map((item) => Number(item.lineNo) || 0)) + 1,
                itemName: payload.item_name || '商品',
                itemType: 'standard',
                quantity,
                unitPrice,
                amount: unitPrice * quantity,
                orderedAt: now,
                orderedTime: '',
                status: 'active',
                backCastId: payload.cast_back_cast_id || null,
                backCastDisplayName: payload.cast_back_display_name || null,
                pending: true
            });
        } else if (operation.operationType === 'void_order') {
            const order = slip.orders.find((item) => String(item.id) === String(payload.order_line_id));
            if (order) {
                order.status = 'voided';
                order.pending = true;
            }
        }

        refreshSlipSummary(slip);
    };

    const projectSnapshot = () => {
        const projected = cloneSnapshot(serverSnapshot);
        if (!projected) return null;
        pendingOperations.forEach((operation) => projectOperation(projected, operation));
        checkoutLockSlipIds.forEach((slipId) => {
            const slip = projected.slips?.find((item) => String(item.id) === String(slipId));
            if (slip && slip.status === 'open') {
                slip.checkoutPending = true;
            }
        });
        const activeSlips = Array.isArray(projected.slips) ? projected.slips.filter((item) => item.status !== 'cancelled') : [];
        projected.openSlipCount = activeSlips.filter((item) => ['open', 'checkout_ready'].includes(item.status)).length;
        projected.checkedOutSlipCount = activeSlips.filter((item) => item.status === 'checked_out').length;
        projected.estimatedSalesAmount = activeSlips.reduce((sum, item) => sum + (Number(item.accountingAmount) || 0), 0);
        return projected;
    };

    const renderProjectedSnapshot = () => {
        const projected = projectSnapshot();
        if (!projected) return;
        slips = Array.isArray(projected.slips) ? projected.slips : [];
        window.prosperBusinessHomeSlips = slips;
        window.prosperBusinessHomeSnapshot = projected;
        updateSummary(projected);
        renderSlips();
        document.dispatchEvent(new CustomEvent('prosper:business-slips-updated', { detail: { slips, snapshot: projected } }));
    };

    const applySnapshot = (snapshot, allowBusinessDayChange = false) => {
        if (!snapshot || typeof snapshot !== 'object') return false;
        const businessDayChanged = serverSnapshot && String(snapshot.businessDayId || '') !== String(serverSnapshot.businessDayId || '');
        if (businessDayChanged && !allowBusinessDayChange) return false;
        if (businessDayChanged) {
            snapshotRevision = -1;
        }
        const revision = Number(snapshot.businessDayRevision);
        if (Number.isFinite(revision) && revision < snapshotRevision) return false;
        if (Number.isFinite(revision)) snapshotRevision = revision;
        serverSnapshot = snapshot;
        hasLoaded = true;
        renderProjectedSnapshot();
        return true;
    };

    const setKaraokeStatus = (state, message) => {
        saveStatus.set(status, state, message);
        if (status) {
            status.hidden = state === 'saved';
        }
    };

    const setAmountVisible = (visible) => {
        document.body.classList.toggle('slip-amounts-visible', visible);
        revealButton?.setAttribute('aria-pressed', visible ? 'true' : 'false');
    };

    const buildElement = (tagName, className, text) => {
        const element = document.createElement(tagName);
        if (className) {
            element.className = className;
        }
        if (text !== undefined && text !== null) {
            element.textContent = String(text);
        }
        return element;
    };
    const isValidBusinessDate = (value) => /^\d{4}-\d{2}-\d{2}$/.test(value || '') && value !== '0001-01-01';

    const getSlip = (slipId) => slips.find((slip) => String(slip.id) === String(slipId));
    const getInitialQuantity = (slip) => toQuantity(slip?.karaokeQuantity);
    const createKaraokeDraftState = () => {
        // 終了・再読み込み後に未送信行を復元しない。営業中の操作中だけメモリに保持する。
        const draft = {};
        const write = () => {};
        const cleanup = () => {
            let changed = false;
            Object.keys(draft).forEach((slipId) => {
                const slip = getSlip(slipId);
                if (!slip || slip.status !== 'open') {
                    delete draft[slipId];
                    changed = true;
                    return;
                }

                if (toQuantity(draft[slipId]) === getInitialQuantity(slip)) {
                    delete draft[slipId];
                    changed = true;
                }
            });

            if (changed) {
                write();
            }
        };

        return {
            getDisplayQuantity(slip) {
                const key = String(slip.id);
                return Object.prototype.hasOwnProperty.call(draft, key)
                    ? toQuantity(draft[key])
                    : getInitialQuantity(slip);
            },
            collectDirtyPayload() {
                cleanup();
                return Object.keys(draft)
                    .map((slipId) => {
                        const slip = getSlip(slipId);
                        if (!slip || slip.status !== 'open') {
                            return null;
                        }

                        return {
                            slip,
                            slipId,
                            quantity: toQuantity(draft[slipId]),
                            baseAmount: Number(slip.accountingAmount || 0) - getInitialQuantity(slip) * accountingUnit
                        };
                    })
                    .filter(Boolean);
            },
            cleanup,
            setQuantity(slip, quantity) {
                const normalized = toQuantity(quantity);
                if (normalized === getInitialQuantity(slip)) {
                    delete draft[String(slip.id)];
                } else {
                    draft[String(slip.id)] = normalized;
                }
                write();
            },
            markSaved(payloadRows) {
                payloadRows.forEach((line) => {
                    const slip = getSlip(line.slipId);
                    if (slip && toQuantity(draft[line.slipId]) === line.quantity) {
                        slip.karaokeQuantity = line.quantity;
                        slip.accountingAmount = line.baseAmount + line.quantity * accountingUnit;
                        delete draft[line.slipId];
                    }
                });
                write();
            },
            markRejected(payloadRows) {
                payloadRows.forEach((line) => {
                    if (toQuantity(draft[line.slipId]) === line.quantity) {
                        delete draft[line.slipId];
                    }
                });
                write();
            },
            write
        };
    };
    const karaokeDraft = createKaraokeDraftState();
    const getDisplayQuantity = (slip) => karaokeDraft.getDisplayQuantity(slip);
    const cleanupDraft = () => karaokeDraft.cleanup();
    const collectDirtyPayload = () => karaokeDraft.collectDirtyPayload();

    const getRequestVerificationToken = () =>
        form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    const buildSaveHeaders = () => {
        const headers = {
            'Accept': 'application/json',
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest'
        };
        const token = getRequestVerificationToken();
        if (token) {
            headers.RequestVerificationToken = token;
        }
        return headers;
    };

    const markDirtyStatus = () => {
        if (collectDirtyPayload().length === 0) {
            setKaraokeStatus('saved', '同期済み');
        } else {
            setKaraokeStatus('dirty');
        }
    };

    const shouldFlushForAnchor = (anchor, event) => {
        if (
            event.defaultPrevented ||
            event.button !== 0 ||
            event.altKey ||
            event.ctrlKey ||
            event.metaKey ||
            event.shiftKey ||
            anchor.hasAttribute('download')
        ) {
            return false;
        }

        const target = anchor.getAttribute('target');
        if (target && target.toLowerCase() !== '_self') {
            return false;
        }

        const rawHref = anchor.getAttribute('href')?.trim();
        if (!rawHref || rawHref === '#') {
            return false;
        }

        const lowerHref = rawHref.toLowerCase();
        if (
            lowerHref.startsWith('javascript:') ||
            lowerHref.startsWith('mailto:') ||
            lowerHref.startsWith('tel:')
        ) {
            return false;
        }

        const url = new URL(anchor.href, window.location.href);
        const isSamePageHash =
            url.origin === window.location.origin &&
            url.pathname === window.location.pathname &&
            url.search === window.location.search &&
            url.hash;

        return !isSamePageHash;
    };

    const updateSummary = (result) => {
        const businessDateText = result?.businessDateDisplay || '';
        const businessDateValue = result?.businessDate || '';
        const businessDayId = result?.businessDayId ? String(result.businessDayId) : '';
        if (businessDateDisplay && businessDateText && !businessDateText.startsWith('0001-01-01')) {
            businessDateDisplay.textContent = businessDateText;
        }
        if (createDateDisplay && isValidBusinessDate(businessDateValue)) {
            createDateDisplay.textContent = businessDateValue;
        }
        if (createDateInput && isValidBusinessDate(businessDateValue)) {
            createDateInput.value = businessDateValue;
        }
        if (createDayIdInput) {
            createDayIdInput.value = businessDayId;
        }
        if (businessDayId) {
            form.dataset.businessDayId = businessDayId;
        }

        if (openSlipCount) {
            openSlipCount.textContent = `${Number(result?.openSlipCount) || 0} 件`;
        }
        if (checkedOutSlipCount) {
            checkedOutSlipCount.textContent = `${Number(result?.checkedOutSlipCount) || 0} 件`;
        }
        if (estimatedSalesAmount) {
            estimatedSalesAmount.textContent = formatYen(Number(result?.estimatedSalesAmount) || 0);
        }
    };

    const renderEmpty = (title, message) => {
        Array.from(list.querySelectorAll('[data-business-slip-row]')).forEach((row) => row.remove());
        let row = list.querySelector('[data-business-empty-row]');
        if (!row) {
            list.innerHTML = '';
            row = buildElement('article', 'slip-list__row slip-list__row--add');
            row.dataset.businessEmptyRow = '';
            const main = buildElement('div', 'slip-list__main');
            const titleElement = buildElement('strong');
            titleElement.dataset.businessEmptyTitle = '';
            const messageElement = buildElement('span');
            messageElement.dataset.businessEmptyMessage = '';
            main.append(titleElement, messageElement);
            row.appendChild(main);

            list.appendChild(row);
        }

        setText(row.querySelector('[data-business-empty-title]'), title);
        setText(row.querySelector('[data-business-empty-message]'), message);
        if (revealButton) {
            revealButton.hidden = true;
        }
    };

    const buildAmountElement = (slip) => {
        const amount = buildElement('span', 'slip-list__amount slip-list__amount--concealed');
        amount.setAttribute('aria-label', '会計額');
        const amountValueElement = buildElement('strong', 'slip-list__amount-value', formatYen(slip.accountingAmount));
        amountValueElement.dataset.businessSlipAmountValue = '';
        amount.append(
            buildElement('span', 'slip-list__amount-mask', '**** 円'),
            amountValueElement
        );
        return amount;
    };

    const syncKaraokeControl = (row, slip) => {
        const amountValue = row.querySelector('[data-business-slip-amount-value]');
        let karaoke = row.querySelector('[data-business-karaoke-row]');
        if (slip.status !== 'open' || slip.checkoutPending) {
            karaoke?.remove();
            setText(amountValue, formatYen(slip.accountingAmount));
            return;
        }

        const initialQuantity = getInitialQuantity(slip);
        const displayQuantity = getDisplayQuantity(slip);
        const baseAmount = Number(slip.accountingAmount || 0) - initialQuantity * accountingUnit;
        if (!karaoke) {
            karaoke = buildElement('div', 'slip-list__karaoke');
            karaoke.dataset.businessKaraokeRow = '';

            const decrement = buildElement('button', 'btn btn-outline-secondary', '-');
            decrement.type = 'button';
            decrement.setAttribute('aria-label', 'カラオケを減らす');
            decrement.dataset.businessKaraokeDecrement = '';
            const increment = buildElement('button', 'btn btn-outline-primary', '+');
            increment.type = 'button';
            increment.setAttribute('aria-label', 'カラオケを増やす');
            increment.dataset.businessKaraokeIncrement = '';
            const quantity = buildElement('strong', null, displayQuantity);
            quantity.dataset.businessKaraokeDisplay = '';
            karaoke.append(decrement, quantity, increment);
        }

        const actions = row.querySelector('.slip-list__actions');
        if (actions && karaoke.parentElement !== actions) {
            actions.prepend(karaoke);
        }

        karaoke.dataset.slipId = String(slip.id);
        karaoke.dataset.initialQuantity = String(initialQuantity);
        karaoke.dataset.quantity = String(displayQuantity);
        karaoke.dataset.accountingBaseAmount = String(baseAmount);
        karaoke.dataset.karaokeAccountingUnit = String(accountingUnit);
        karaoke.classList.toggle('is-dirty', displayQuantity !== initialQuantity);
        setText(karaoke.querySelector('[data-business-karaoke-display]'), displayQuantity);
        setText(amountValue, formatYen(baseAmount + displayQuantity * accountingUnit));
    };

    const buildSlipDetails = () => {
        const panel = buildElement('section', 'slip-list__details');
        panel.dataset.businessSlipDetails = '';
        const fields = buildElement('div', 'slip-list__details-content');
        fields.dataset.businessSlipDetailsContent = '';
        panel.appendChild(fields);
        return panel;
    };

    const buildSlipDetailActions = () => {
        const actions = buildElement('div', 'business-slip-detail-actions');
        const orderButton = buildElement('button', 'btn btn-sm btn-primary', '注文を編集');
        orderButton.type = 'button';
        orderButton.dataset.businessSlipEditor = 'orders';
        const adjustmentButton = buildElement('button', 'btn btn-sm btn-outline-primary', '自由明細を編集');
        adjustmentButton.type = 'button';
        adjustmentButton.dataset.businessSlipEditor = 'adjustments';
        actions.append(orderButton, adjustmentButton);
        return actions;
    };

    const orderSection = (slip) => {
        const section = buildElement('section', 'business-slip-detail-orders');
        section.setAttribute('aria-label', '注文と自由明細');
        const body = buildElement('div', 'business-slip-detail-orders__body');
        renderOrders(body, slip);
        renderAdjustments(body, slip);
        section.append(body, buildSlipDetailActions());
        return section;
    };

    const detailLine = (primary, secondary, amount, pending = false) => {
        const line = buildElement('div', 'business-slip-detail-line');
        if (pending) line.classList.add('is-pending');
        const main = buildElement('div', 'business-slip-detail-line__main');
        main.append(buildElement('strong', null, primary));
        if (secondary) main.append(buildElement('span', null, secondary));
        line.append(main);
        if (amount !== undefined && amount !== null) line.append(buildElement('strong', 'business-slip-detail-line__amount', amount));
        return line;
    };

    const detailSummary = (heading, kind, content, editorLabel) => {
        const row = buildElement('div', `business-slip-detail-summary business-slip-detail-summary--${kind}`);
        row.append(buildElement('strong', 'business-slip-detail-summary__heading', heading), content);
        const button = buildElement('button', 'btn btn-sm btn-outline-primary', editorLabel);
        button.type = 'button';
        button.dataset.businessSlipEditor = kind;
        row.appendChild(button);
        return row;
    };

    const customerSummary = (slip) => {
        const customers = Array.isArray(slip.customers) ? slip.customers : [];
        const content = buildElement('div', 'business-slip-detail-summary__content');
        if (customers.length === 0) {
            content.appendChild(buildElement('strong', 'business-slip-detail-summary__empty', '在席客なし'));
        } else {
            customers.forEach((customer) => {
                const chip = buildElement('strong', 'business-slip-detail-summary__chip', customer.displayName || '客名なし');
                if (customer.status !== 'active') chip.classList.add('is-departed');
                content.appendChild(chip);
            });
        }
        return detailSummary('客', 'customers', content, '客を編集');
    };

    const nominationSummary = (slip) => {
        const nominations = Array.isArray(slip.nominations) ? slip.nominations.filter((nomination) => nomination.status === 'active') : [];
        const content = buildElement('div', 'business-slip-detail-summary__content');
        if (nominations.length === 0) {
            content.appendChild(buildElement('strong', 'business-slip-detail-summary__empty', '指名なし'));
        } else {
            nominations.forEach((nomination) => {
                const pair = buildElement('strong', 'business-slip-detail-summary__chip');
                const kind = nomination.nominationDisplayName || nomination.nominationKind || '指名';
                pair.textContent = `${nomination.displayName || 'キャスト'} — ${kind}`;
                content.appendChild(pair);
            });
        }
        return detailSummary('指名', 'nominations', content, '指名を編集');
    };

    const renderOrderGroup = (target, slip, key, label, lines) => {
        const groupKey = `${slip.id}:${key}`;
        const expanded = expandedOrderGroupKeys.has(groupKey);
        const totalQuantity = lines.reduce((sum, line) => sum + (Number(line.quantity) || 0), 0);
        const totalAmount = lines.reduce((sum, line) => sum + (Number(line.amount) || 0), 0);
        const group = buildElement('section', 'business-slip-order-group');
        const header = buildElement('button', 'business-slip-order-group__header');
        header.type = 'button';
        header.dataset.businessOrderGroupToggle = groupKey;
        header.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        const backName = lines[0]?.backCastDisplayName ? ` / ${lines[0].backCastDisplayName}` : '';
        header.append(
            buildElement('strong', null, `${label}${backName}`),
            buildElement('span', null, `* ${totalQuantity}点`),
            buildElement('strong', null, formatYen(totalAmount))
        );
        group.appendChild(header);

        if (expanded) {
            const events = buildElement('div', 'business-slip-order-group__events');
            lines.forEach((line) => {
                events.append(detailLine(
                    `${line.orderedTime || '-'} / ${line.itemName || '-'}`,
                    `${formatYen(line.unitPrice)} × ${line.quantity || 0}${line.backCastDisplayName ? ` / ${line.backCastDisplayName}` : ''}`,
                    formatYen(line.amount),
                    line.pending
                ));
            });
            group.appendChild(events);
        }

        target.appendChild(group);
    };

    const renderOrders = (target, slip) => {
        target.replaceChildren();
        const orders = (Array.isArray(slip.orders) ? slip.orders : []).filter((line) => line.status === 'active');
        if (orders.length === 0) {
            target.appendChild(buildElement('span', 'business-slip-detail-summary__empty', '注文はありません。'));
            return;
        }

        const standard = orders.filter((line) => (line.itemType || 'standard') === 'standard');
        const automatic = orders.filter((line) => (line.itemType || 'standard') !== 'standard');
        const renderGroups = (lines, prefix, keyBuilder) => {
            const groups = new Map();
            lines.forEach((line) => {
                const key = keyBuilder(line);
                const group = groups.get(key) || [];
                group.push(line);
                groups.set(key, group);
            });
            groups.forEach((groupLines, key) => renderOrderGroup(target, slip, `${prefix}:${key}`, groupLines[0]?.itemName || '-', groupLines));
        };

        renderGroups(standard, 'order', (line) => `${line.itemName || ''}\u0000${Number(line.unitPrice) || 0}\u0000${line.backCastId || ''}`);
        renderGroups(automatic, 'auto', (line) => `${line.itemName || ''}\u0000${Number(line.unitPrice) || 0}`);
    };

    const renderAdjustments = (target, slip) => {
        const adjustments = Array.isArray(slip.adjustments) ? slip.adjustments.filter((item) => item.status === 'active') : [];
        adjustments.forEach((adjustment) => {
            target.append(detailLine(adjustment.lineName || '-', adjustment.createdTime || '-', formatSignedYen(adjustment.amount), adjustment.pending));
        });
    };

    const buildAccountingTotals = (slip) => {
        const orderSubtotal = Math.round(Number(slip.orderSubtotalAmount) || 0);
        const pricingSubtotal = Math.round(Number(slip.pricingSubtotalAmount) || 0);
        const subtotal = orderSubtotal + pricingSubtotal;
        const serviceCharge = Math.round(subtotal * 0.20);
        const total = Math.round(Number(slip.accountingAmount) || 0);
        const totals = buildElement('section', 'business-slip-detail-totals');
        totals.setAttribute('aria-label', '会計内訳');

        [
            ['小計', subtotal, false],
            ['サービス料', serviceCharge, false],
            ['合計', total, true]
        ].forEach(([label, amount, emphasized]) => {
            const line = buildElement('div', 'business-slip-detail-totals__line');
            if (emphasized) line.classList.add('business-slip-detail-totals__line--total');
            line.append(buildElement('span', null, label), buildElement('strong', null, formatYen(amount)));
            totals.appendChild(line);
        });

        return totals;
    };

    const syncSlipDetails = (row, slip) => {
        const panel = row.querySelector('[data-business-slip-details]');
        const toggle = row.querySelector('[data-business-slip-details-toggle]');
        if (!panel || !toggle) {
            return;
        }

        const isExpanded = expandedSlipIds.has(String(slip.id));
        const panelId = `business-slip-details-${slip.id}`;
        panel.id = panelId;
        panel.setAttribute('aria-label', `卓 ${slip.tableDisplay} の詳細`);
        panel.hidden = !isExpanded;
        row.classList.toggle('slip-list__row--expanded', isExpanded);
        toggle.dataset.businessSlipDetailsToggle = String(slip.id);
        toggle.setAttribute('aria-controls', panelId);
        toggle.setAttribute('aria-expanded', isExpanded ? 'true' : 'false');
        toggle.setAttribute('aria-label', isExpanded ? '詳細を閉じる' : '詳細を開く');
        toggle.textContent = isExpanded ? '∧' : '∨';
        const content = panel.querySelector('[data-business-slip-details-content]');
        if (content) {
            content.replaceChildren();
            const layout = buildElement('div', 'business-slip-detail-layout');
            const activity = buildElement('div', 'business-slip-detail-layout__activity');
            const accounting = buildElement('aside', 'business-slip-detail-layout__accounting');
            accounting.setAttribute('aria-label', '注文と会計');
            activity.append(customerSummary(slip), nominationSummary(slip));
            accounting.append(orderSection(slip), buildAccountingTotals(slip));
            layout.append(activity, accounting);
            content.appendChild(layout);
        }
        const canEdit = slip.status === 'open' && !slip.checkoutPending;
        panel.querySelectorAll('[data-business-slip-editor]').forEach((button) => {
            button.hidden = !canEdit;
            if (canEdit) {
                button.dataset.businessSlipId = String(slip.id);
            } else {
                delete button.dataset.businessSlipId;
            }
        });
        const pending = pendingForSlip(slip.id);
        const pendingState = row.querySelector('[data-business-slip-sync-state]');
        if (pendingState) {
            pendingState.hidden = pending.length === 0 && !slip.checkoutPending;
            const isSyncing = pending.some((operation) => operation.state === 'saving');
            pendingState.textContent = slip.checkoutPending
                ? '会計準備中'
                : isSyncing
                    ? `同期中 ${pending.length}`
                    : `保存待ち ${pending.length}`;
            pendingState.classList.toggle('is-unknown', false);
        }
    };

    const syncSlipRow = (row, slip) => {
        row.dataset.slipId = String(slip.id);
        const isOpen = slip.status === 'open' && !slip.checkoutPending;
        row.classList.toggle('slip-list__row--open', isOpen);
        row.classList.toggle('slip-list__row--not-open', !isOpen);
        row.classList.toggle('slip-list__row--checkout-ready', slip.status === 'checkout_ready' || Boolean(slip.checkoutPending));
        row.classList.toggle('slip-list__row--checked-out', slip.status === 'checked_out');
        row.setAttribute('aria-label', `${slip.tableDisplay} ${slip.checkoutPending ? '会計準備中' : slip.statusDisplay}`);

        setText(row.querySelector('[data-business-slip-table]'), slip.tableDisplay);
        setText(row.querySelector('[data-business-slip-time]'), slip.openedTime);
        setText(row.querySelector('[data-business-slip-customers]'), slip.customerNames || '客名なし');
        setText(row.querySelector('[data-business-slip-casts]'), slip.castNames || '指名なし');
        const checkoutButton = row.querySelector('[data-business-start-checkout]');
        const receiptButton = row.querySelector('[data-business-print-receipt]');
        const cancelButton = row.querySelector('[data-business-cancel-checkout]');
        if (checkoutButton) {
            checkoutButton.hidden = !['open', 'checkout_ready'].includes(slip.status);
            checkoutButton.dataset.businessStartCheckout = String(slip.id);
            checkoutButton.disabled = Boolean(slip.checkoutPending);
            checkoutButton.textContent = slip.checkoutPending
                ? '会計準備中'
                : slip.status === 'checkout_ready' ? '決済' : '会計伝票';
        }
        if (receiptButton) {
            receiptButton.hidden = slip.status !== 'checked_out';
            receiptButton.dataset.businessPrintReceipt = String(slip.id);
        }
        if (cancelButton) {
            cancelButton.hidden = slip.status !== 'checked_out';
            cancelButton.dataset.businessCancelCheckout = String(slip.id);
        }
        syncKaraokeControl(row, slip);
        syncSlipDetails(row, slip);
    };

    const buildSlipRow = (slip) => {
        const row = buildElement('article', 'slip-list__row slip-list__row--action');
        row.dataset.businessSlipRow = '';
        const main = buildElement('div', 'slip-list__row-main');

        const table = buildElement('strong', 'slip-list__table');
        table.dataset.businessSlipTable = '';
        const openedTime = buildElement('span', 'slip-list__time');
        openedTime.dataset.businessSlipTime = '';
        const customers = buildElement('span', 'slip-list__customers');
        customers.dataset.businessSlipCustomers = '';
        const casts = buildElement('span', 'slip-list__casts');
        casts.dataset.businessSlipCasts = '';
        const syncState = buildElement('span', 'slip-list__sync-state');
        syncState.dataset.businessSlipSyncState = '';
        syncState.hidden = true;

        main.append(table, openedTime, customers, casts, syncState);
        main.appendChild(buildAmountElement(slip));
        const actions = buildElement('div', 'slip-list__actions');
        const actionButtons = buildElement('div', 'slip-list__action-buttons');
        const detailsToggle = buildElement('button', 'btn btn-sm btn-outline-secondary slip-list__details-toggle', '∨');
        detailsToggle.type = 'button';
        detailsToggle.setAttribute('aria-label', '詳細を開く');
        detailsToggle.dataset.businessSlipDetailsToggle = '';
        const checkoutButton = buildElement('button', 'btn btn-sm btn-primary slip-list__checkout-action');
        checkoutButton.type = 'button';
        checkoutButton.dataset.businessStartCheckout = '';
        const receiptButton = buildElement('button', 'btn btn-sm btn-outline-primary', '領収書');
        receiptButton.type = 'button';
        receiptButton.dataset.businessPrintReceipt = '';
        const cancelButton = buildElement('button', 'btn btn-sm btn-outline-danger slip-list__checkout-action', '会計取消');
        cancelButton.type = 'button';
        cancelButton.dataset.businessCancelCheckout = '';
        actionButtons.append(detailsToggle, checkoutButton, receiptButton, cancelButton);
        actions.appendChild(actionButtons);
        row.append(main, actions, buildSlipDetails());

        syncSlipRow(row, slip);

        return row;
    };

    const renderSlips = () => {
        cleanupDraft();
        expandedSlipIds.forEach((slipId) => {
            if (!getSlip(slipId)) {
                expandedSlipIds.delete(slipId);
            }
        });
        if (slips.length === 0) {
            renderEmpty('当日の伝票はまだありません', '最初の伝票作成時に営業日を自動作成します。');
            return;
        }

        Array.from(list.children).forEach((child) => {
            if (!child.matches('[data-business-slip-row]')) {
                child.remove();
            }
        });

        const rowsBySlipId = new Map(
            Array.from(list.querySelectorAll('[data-business-slip-row]'))
                .map((row) => [row.dataset.slipId, row])
        );
        const renderedSlipIds = new Set();
        slips.forEach((slip, index) => {
            const slipId = String(slip.id);
            const existingRow = rowsBySlipId.get(slipId);
            const row = existingRow ?? buildSlipRow(slip);
            if (existingRow) {
                syncSlipRow(row, slip);
            }

            renderedSlipIds.add(slipId);
            const current = list.children[index] ?? null;
            if (current !== row) {
                list.insertBefore(row, current);
            }
        });

        rowsBySlipId.forEach((row, slipId) => {
            if (!renderedSlipIds.has(slipId)) {
                row.remove();
            }
        });
        if (revealButton) {
            revealButton.hidden = false;
        }
    };

    const loadSlips = () => {
        if (refreshPromise) {
            return refreshPromise;
        }

        refreshPromise = (async () => {
            try {
                const response = await fetch(businessSlipsUrl, {
                    headers: {
                        'Accept': 'application/json'
                    }
                });
                if (!response.ok) {
                    throw new Error('Business slips load failed.');
                }

                const result = await response.json();
                const snapshot = result?.snapshot ?? result;
                if (!result?.succeeded || !applySnapshot(snapshot, true)) {
                    throw new Error(result?.message || 'Business snapshot load failed.');
                }
                if (!isSaving) {
                    markDirtyStatus();
                }
                return true;
            } catch {
                if (!hasLoaded) {
                    renderEmpty('伝票を取得できませんでした', '次の自動更新で再取得します。');
                }
                return false;
            } finally {
                refreshPromise = null;
            }
        })();

        return refreshPromise;
    };

    const createClientId = () => window.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`;
    const hasDirtyKaraoke = () => collectDirtyPayload().length > 0;
    const hasUnconfirmedChanges = () => pendingFlushBatch !== null || hasPendingOperations() || hasDirtyKaraoke();

    const createFlushBatch = () => {
        const operations = Array.from(pendingOperations.values())
            .filter((operation) => operation.state !== 'saving');
        const karaokeLines = collectDirtyPayload().map((line) => ({
            draftId: createClientId(),
            slipId: Number(line.slipId),
            quantity: line.quantity,
            baseAmount: line.baseAmount
        }));
        if (operations.length === 0 && karaokeLines.length === 0) return null;

        operations.forEach((operation) => { operation.state = 'saving'; });
        return {
            batchId: createClientId(),
            operations,
            karaokeLines
        };
    };

    const friendlyFlushError = (message, fallback) => {
        const raw = String(message || '');
        if (raw.includes('store_slip_not_found')) return '対象の伝票は編集できません。';
        if (raw.includes('store_slip_customer_not_found')) return '対象の客を確認してください。';
        if (raw.includes('store_slip_nomination_not_found')) return '対象の指名を確認してください。';
        if (raw.includes('store_slip_adjustment_not_found')) return '対象の自由明細を確認してください。';
        if (raw.includes('store_order_line_not_found')) return '対象の注文を確認してください。';
        if (raw.includes('invalid_customer_label')) return '客名は100文字以内で入力してください。';
        if (raw.includes('invalid_customer_time') || raw.includes('invalid_left_at')) return '入退店時刻を確認してください。';
        if (raw.includes('invalid_karaoke_quantity')) return 'カラオケ回数を確認してください。';
        if (raw.includes('invalid_order_quantity')) return '注文数量を確認してください。';
        if (raw.includes('invalid_adjustment_')) return '自由明細の内容を確認してください。';
        return fallback;
    };

    const queueNoticeForFailedRows = (operationResults, karaokeResults) => {
        const messages = [];
        operationResults.filter((row) => row?.succeeded === false).forEach((row) => {
            messages.push(friendlyFlushError(row.message, '編集内容を保存できませんでした。'));
        });
        karaokeResults.filter((row) => row?.succeeded === false).forEach((row) => {
            messages.push(friendlyFlushError(row.message, 'カラオケ回数を保存できませんでした。'));
        });
        if (messages.length > 0) showOperationNotice([...new Set(messages)].join(' '));
    };

    const applyFlushResult = (batch, result) => {
        const operationResults = Array.isArray(result?.operationResults) ? result.operationResults : [];
        const karaokeResults = Array.isArray(result?.karaokeResults) ? result.karaokeResults : [];
        const operationResultIds = new Set(operationResults.map((row) => String(row?.operation_id || '')));
        const karaokeResultIds = new Set(karaokeResults.map((row) => String(row?.draft_id || '')));

        if (batch.operations.some((operation) => !operationResultIds.has(String(operation.operationId))) ||
            batch.karaokeLines.some((line) => !karaokeResultIds.has(String(line.draftId)))) {
            return false;
        }

        batch.operations.forEach((operation) => pendingOperations.delete(operation.operationId));
        karaokeResults.filter((row) => row?.succeeded).forEach((row) => {
            const line = batch.karaokeLines.find((candidate) => String(candidate.draftId) === String(row.draft_id));
            if (line) karaokeDraft.markSaved([line]);
        });
        karaokeResults.filter((row) => row?.succeeded === false).forEach((row) => {
            const line = batch.karaokeLines.find((candidate) => String(candidate.draftId) === String(row.draft_id));
            if (line) karaokeDraft.markRejected([line]);
        });
        applySnapshot(result.snapshot);
        queueNoticeForFailedRows(operationResults, karaokeResults);
        return true;
    };

    const flushBusinessHomeChanges = async () => {
        if (flushPromise) return flushPromise;
        if (!flushBusinessHomeChangesUrl) {
            showOperationNotice('営業中の保存先を取得できません。');
            return false;
        }

        pendingFlushBatch ??= createFlushBatch();
        if (!pendingFlushBatch) {
            markDirtyStatus();
            return true;
        }

        const batch = pendingFlushBatch;
        isSaving = true;
        setKaraokeStatus('saving');
        renderProjectedSnapshot();

        flushPromise = (async () => {
            const controller = new AbortController();
            const timeout = window.setTimeout(() => controller.abort(), operationTimeoutMs);
            try {
                const response = await fetch(flushBusinessHomeChangesUrl, {
                    method: 'POST',
                    signal: controller.signal,
                    headers: buildSaveHeaders(),
                    body: JSON.stringify({
                        batchId: batch.batchId,
                        operations: batch.operations.map((operation) => ({
                            operationId: operation.operationId,
                            slipId: Number(operation.slipId),
                            operationType: operation.operationType,
                            payload: operation.payload
                        })),
                        karaokeLines: batch.karaokeLines.map((line) => ({
                            draftId: line.draftId,
                            slipId: line.slipId,
                            quantity: line.quantity
                        }))
                    })
                });
                const result = await response.json().catch(() => null);
                if (!response.ok || !result?.succeeded || !result?.snapshot || !applyFlushResult(batch, result)) {
                    showOperationNotice(result?.message || '保存結果を確認できません。通信復旧後に同じ変更を再送します。');
                    return false;
                }

                pendingFlushBatch = null;
                renderProjectedSnapshot();
                markDirtyStatus();
                return true;
            } catch {
                showOperationNotice('保存結果を確認できません。通信復旧後に同じ変更を再送します。');
                return false;
            } finally {
                window.clearTimeout(timeout);
                isSaving = false;
                flushPromise = null;
                if (pendingFlushBatch !== batch) {
                    markDirtyStatus();
                } else {
                    setKaraokeStatus('error');
                }
            }
        })();

        return flushPromise;
    };

    const scheduleFlush = () => {
        if (flushTimer || flushPromise) return;
        flushTimer = window.setTimeout(() => {
            flushTimer = null;
            void flushBusinessHomeChanges().then((saved) => {
                if (saved && hasUnconfirmedChanges()) scheduleFlush();
            });
        }, 0);
    };

    const enqueueEditorOperation = (operation) => {
        const normalized = {
            ...operation,
            operationId: operation.operationId || createClientId(),
            state: 'queued'
        };
        pendingOperations.set(normalized.operationId, normalized);
        renderProjectedSnapshot();
        scheduleFlush();
        return normalized.operationId;
    };

    const setCheckoutLock = (slipId, locked) => {
        if (locked) {
            checkoutLockSlipIds.add(String(slipId));
        } else {
            checkoutLockSlipIds.delete(String(slipId));
        }
        renderProjectedSnapshot();
    };

    const waitForBusinessOperations = async () => {
        if (!hasUnconfirmedChanges()) return true;
        const saved = await flushBusinessHomeChanges();
        if (!saved) return false;
        return hasUnconfirmedChanges() ? waitForBusinessOperations() : true;
    };

    const submitAfterFlush = async (targetForm, submitter) => {
        window.AppLoading?.show(targetForm);
        if (!await waitForBusinessOperations()) {
            window.AppLoading?.hide(targetForm);
            showOperationNotice('保存結果を確認できない操作があります。同期後に移動してください。');
            return;
        }

        window.AppLoading?.hide(targetForm);
        allowNextPageUnload();
        targetForm.dataset.karaokeFlushBypass = 'true';
        if (typeof targetForm.requestSubmit === 'function') {
            targetForm.requestSubmit(submitter || undefined);
        } else {
            targetForm.submit();
        }
    };

    form.addEventListener('click', (event) => {
        const orderGroupToggle = event.target.closest('[data-business-order-group-toggle]');
        if (orderGroupToggle) {
            const groupKey = orderGroupToggle.dataset.businessOrderGroupToggle;
            if (expandedOrderGroupKeys.has(groupKey)) {
                expandedOrderGroupKeys.delete(groupKey);
            } else {
                expandedOrderGroupKeys.add(groupKey);
            }

            const row = orderGroupToggle.closest('[data-business-slip-row]');
            const slip = row ? getSlip(row.dataset.slipId) : null;
            if (row && slip) {
                syncSlipDetails(row, slip);
            }
            return;
        }

        const detailsToggle = event.target.closest('[data-business-slip-details-toggle]');
        if (detailsToggle) {
            const slip = getSlip(detailsToggle.dataset.businessSlipDetailsToggle);
            const row = detailsToggle.closest('[data-business-slip-row]');
            if (!slip || !row) {
                return;
            }

            const slipId = String(slip.id);
            if (expandedSlipIds.has(slipId)) {
                expandedSlipIds.delete(slipId);
            } else {
                expandedSlipIds.clear();
                expandedOrderGroupKeys.clear();
                expandedSlipIds.add(slipId);
            }
            renderSlips();
            return;
        }

        const decrement = event.target.closest('[data-business-karaoke-decrement]');
        const increment = event.target.closest('[data-business-karaoke-increment]');
        if (decrement || increment) {
            const row = event.target.closest('[data-business-karaoke-row]');
            if (!row) {
                return;
            }

            const slip = getSlip(row.dataset.slipId);
            if (!slip) {
                return;
            }

            const nextQuantity = toQuantity(row.dataset.quantity) + (increment ? 1 : -1);
            karaokeDraft.setQuantity(slip, nextQuantity);
            renderSlips();
            markDirtyStatus();
            return;
        }
    });

    form.addEventListener('submit', (event) => {
        event.preventDefault();
        window.AppLoading?.show(form);
        void flushBusinessHomeChanges().finally(() => window.AppLoading?.hide(form));
    });

    document.addEventListener('submit', (event) => {
        const targetForm = event.target;
        if (!(targetForm instanceof HTMLFormElement) || targetForm === form) {
            return;
        }

        // The editor queues its own operation before flushing, so that operation and
        // any dirty karaoke quantity are persisted by the same batch RPC.
        if (targetForm.matches('[data-business-slip-editor-form]')) {
            return;
        }

        if (targetForm.dataset.karaokeFlushBypass === 'true') {
            delete targetForm.dataset.karaokeFlushBypass;
            return;
        }

        if (event.defaultPrevented || !targetForm.checkValidity() || !hasUnconfirmedChanges()) {
            return;
        }

        event.preventDefault();
        void submitAfterFlush(targetForm, event.submitter);
    }, true);

    document.addEventListener('click', (event) => {
        const anchor = event.target.closest('a[href]');
        if (!anchor || !shouldFlushForAnchor(anchor, event)) {
            return;
        }

        const destination = new URL(anchor.href, window.location.href);
        if (destination.origin !== window.location.origin || destination.pathname.toLowerCase().includes('/logout') || !hasUnconfirmedChanges()) {
            return;
        }

        event.preventDefault();
        if (navigationInFlight) {
            return;
        }

        navigationInFlight = true;
        window.AppLoading?.show(anchor);
        void waitForBusinessOperations().then((synchronized) => {
            if (!synchronized) {
                navigationInFlight = false;
                window.AppLoading?.hide(anchor);
                return;
            }

            window.location.assign(anchor.href);
        });
    }, true);

    window.addEventListener('keydown', (event) => {
        if (event.key !== 'F5' && !(event.key.toLowerCase() === 'r' && (event.ctrlKey || event.metaKey))) {
            return;
        }

        if (hasUnconfirmedChanges()) {
            event.preventDefault();
            showOperationNotice('保存中の変更があります。同期完了後に再読み込みできます。');
            return;
        }

        event.preventDefault();
        markDirtyStatus();
    });

    revealButton?.addEventListener('pointerdown', () => setAmountVisible(true));
    document.addEventListener('pointerup', () => setAmountVisible(false));
    document.addEventListener('pointercancel', () => setAmountVisible(false));
    revealButton?.addEventListener('blur', () => setAmountVisible(false));
    revealButton?.addEventListener('keydown', (event) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            setAmountVisible(true);
        }
    });
    revealButton?.addEventListener('keyup', (event) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            setAmountVisible(false);
        }
    });

    window.addEventListener('online', () => {
        markDirtyStatus();
        void loadSlips();
        scheduleFlush();
    });
    window.addEventListener('focus', () => {
        if (document.visibilityState === 'visible') {
            void loadSlips();
        }
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            void loadSlips();
        }
    });
    window.setInterval(() => {
        if (document.visibilityState === 'visible') {
            void loadSlips();
        }
    }, refreshIntervalMs);

    void loadSlips();
    window.prosperBusinessHomeReload = loadSlips;
    window.prosperBusinessHomeFlushKaraoke = flushBusinessHomeChanges;
    window.prosperBusinessHomeEnqueueEditorOperation = enqueueEditorOperation;
    window.prosperBusinessHomeSetCheckoutLock = setCheckoutLock;
    window.prosperBusinessHomeGetPendingForSlip = pendingForSlip;
    window.prosperBusinessHomeWaitForOperations = waitForBusinessOperations;
})();
