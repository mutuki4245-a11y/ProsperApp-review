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
    const businessSlipEditorOperationUrl = config.businessSlipEditorOperationUrl || '';
    const draftKey = `prosper:business:${form.dataset.businessDayId || 'current'}:karaoke`;
    const refreshIntervalMs = 10000;
    const accountingUnit = 240;
    let slips = [];
    let serverSnapshot = null;
    let snapshotRevision = -1;
    const pendingOperations = new Map();
    const checkoutLockSlipIds = new Set();
    const operationLanes = new Map();
    const expandedOrderGroupKeys = new Set();
    const expandedSlipIds = new Set();
    let hasLoaded = false;
    let refreshPromise = null;
    let isSaving = false;
    let savePromise = null;
    let allowPageUnload = false;
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
    const operationLane = (operation) => {
        const section = ['add_nomination', 'cancel_nomination'].includes(operation.operationType)
            ? 'nominations'
            : ['add_adjustment', 'void_adjustment'].includes(operation.operationType)
                ? 'adjustments'
                : ['add_order', 'void_order'].includes(operation.operationType)
                    ? 'orders'
                    : 'customers';
        return `${operation.slipId}:${section}`;
    };

    const pendingForSlip = (slipId) => Array.from(pendingOperations.values())
        .filter((operation) => String(operation.slipId) === String(slipId));

    const hasPendingOperations = () => pendingOperations.size > 0;

    const isUnknownOperation = (operation) => operation.state === 'unknown';

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
        const customers = Array.isArray(slip.customers) ? slip.customers.filter((item) => item.status !== 'cancelled') : [];
        const nominations = Array.isArray(slip.nominations) ? slip.nominations.filter((item) => item.status !== 'cancelled') : [];
        const orders = Array.isArray(slip.orders) ? slip.orders.filter((item) => item.status === 'active') : [];
        const adjustments = Array.isArray(slip.adjustments) ? slip.adjustments.filter((item) => item.status === 'active') : [];
        const names = customers.map((item) => item.displayName || `客${item.lineNo || ''}`).filter(Boolean);
        const castNames = [];
        nominations.forEach((item) => {
            if (item.displayName && !castNames.includes(item.displayName)) castNames.push(item.displayName);
        });
        slip.customerCount = customers.filter((item) => item.status === 'active').length;
        slip.customerNames = names.join('、') || '客名なし';
        slip.castNames = castNames.join('、') || '指名なし';
        slip.orderCount = orders.length;
        slip.orderSubtotalAmount = orders.reduce((sum, item) => sum + (Number(item.amount) || 0), 0);
        slip.adjustmentAmount = adjustments.reduce((sum, item) => sum + (Number(item.amount) || 0), 0);
        slip.karaokeQuantity = orders
            .filter((item) => item.itemType === 'karaoke')
            .reduce((sum, item) => sum + (Number(item.quantity) || 0), 0);
        slip.accountingAmount = Math.max(0, slip.orderSubtotalAmount + Math.round(slip.orderSubtotalAmount * 0.20) + slip.adjustmentAmount);
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

    function loadDraft() {
        const raw = localStorage.getItem(draftKey);
        if (!raw) {
            return {};
        }

        try {
            const parsed = JSON.parse(raw);
            return parsed && typeof parsed === 'object' ? parsed : {};
        } catch {
            localStorage.removeItem(draftKey);
            return {};
        }
    }

    const getSlip = (slipId) => slips.find((slip) => String(slip.id) === String(slipId));
    const getInitialQuantity = (slip) => toQuantity(slip?.karaokeQuantity);
    const createKaraokeDraftState = () => {
        let draft = loadDraft();
        const write = () => {
            if (Object.keys(draft).length === 0) {
                localStorage.removeItem(draftKey);
            } else {
                localStorage.setItem(draftKey, JSON.stringify(draft));
            }
        };
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
            write
        };
    };
    const karaokeDraft = createKaraokeDraftState();
    const getDisplayQuantity = (slip) => karaokeDraft.getDisplayQuantity(slip);
    const cleanupDraft = () => karaokeDraft.cleanup();
    const collectDirtyPayload = () => karaokeDraft.collectDirtyPayload();

    const getRequestVerificationToken = () =>
        form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    const buildSavePayload = (payloadRows) => ({
        businessDayId: Number(form.dataset.businessDayId || 0) || null,
        karaokeLines: payloadRows.map((line) => ({
            slipId: Number(line.slipId),
            quantity: line.quantity
        }))
    });

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

    const allowNextPageUnload = () => {
        allowPageUnload = true;
        window.setTimeout(() => {
            allowPageUnload = false;
        }, 1500);
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
        const actions = buildElement('div', 'slip-list__details-actions');
        const orderButton = buildElement('button', 'btn btn-sm btn-primary', '注文を追加');
        orderButton.type = 'button';
        orderButton.dataset.businessSlipEditor = 'orders';
        const adjustmentButton = buildElement('button', 'btn btn-sm btn-outline-primary', '自由明細を編集');
        adjustmentButton.type = 'button';
        adjustmentButton.dataset.businessSlipEditor = 'adjustments';
        actions.append(orderButton, adjustmentButton);
        return actions;
    };

    const detailSection = (title) => {
        const section = buildElement('section', 'business-slip-detail-section');
        section.append(buildElement('h4', 'business-slip-detail-section__title', title));
        section.appendChild(buildElement('div', 'business-slip-detail-section__divider'));
        const body = buildElement('div', 'business-slip-detail-section__body');
        body.dataset.businessSlipDetailBody = title;
        section.appendChild(body);
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
        const customers = Array.isArray(slip.customers) ? slip.customers.filter((customer) => customer.status === 'active') : [];
        const content = buildElement('div', 'business-slip-detail-summary__content');
        if (customers.length === 0) {
            content.appendChild(buildElement('strong', 'business-slip-detail-summary__empty', '在席客なし'));
        } else {
            customers.forEach((customer) => {
                content.appendChild(buildElement('strong', 'business-slip-detail-summary__chip', customer.displayName || '客名なし'));
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
        const backName = lines[0]?.backCastDisplayName ? ` / バック: ${lines[0].backCastDisplayName}` : '';
        header.append(
            buildElement('strong', null, label),
            buildElement('span', null, `${totalQuantity}点${backName}`),
            buildElement('strong', null, formatYen(totalAmount))
        );
        group.appendChild(header);
        if (expanded) {
            const events = buildElement('div', 'business-slip-order-group__events');
            lines.forEach((line) => {
                events.append(detailLine(
                    `${line.orderedTime || '-'} / ${line.itemName || '-'}`,
                    `${formatYen(line.unitPrice)} × ${line.quantity || 0}${line.backCastDisplayName ? ` / バック: ${line.backCastDisplayName}` : ''}`,
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
            target.textContent = '注文はありません。';
            return;
        }
        const standard = orders.filter((line) => (line.itemType || 'standard') === 'standard');
        const automatic = orders.filter((line) => (line.itemType || 'standard') !== 'standard');
        const groups = new Map();
        standard.forEach((line) => {
            const key = `${line.itemName || ''}\u0000${Number(line.unitPrice) || 0}\u0000${line.backCastId || ''}`;
            const group = groups.get(key) || [];
            group.push(line);
            groups.set(key, group);
        });
        groups.forEach((lines, key) => renderOrderGroup(target, slip, `order:${key}`, lines[0]?.itemName || '-', lines));
        if (automatic.length > 0) {
            const autoGroups = new Map();
            automatic.forEach((line) => {
                const key = `${line.itemName || ''}\u0000${Number(line.unitPrice) || 0}`;
                const group = autoGroups.get(key) || [];
                group.push(line);
                autoGroups.set(key, group);
            });
            autoGroups.forEach((lines, key) => renderOrderGroup(target, slip, `auto:${key}`, lines[0]?.itemName || '-', lines));
        }
    };

    const renderAdjustments = (target, slip) => {
        const adjustments = Array.isArray(slip.adjustments) ? slip.adjustments.filter((item) => item.status === 'active') : [];
        adjustments.forEach((adjustment) => {
            target.append(detailLine(adjustment.lineName || '-', adjustment.createdTime || '-', formatSignedYen(adjustment.amount), adjustment.pending));
        });
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
            const orderSection = detailSection('注文');
            renderOrders(orderSection.querySelector('[data-business-slip-detail-body]'), slip);
            renderAdjustments(orderSection.querySelector('[data-business-slip-detail-body]'), slip);
            orderSection.appendChild(buildSlipDetailActions());
            content.append(customerSummary(slip), nominationSummary(slip), orderSection);
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
            pendingState.textContent = slip.checkoutPending
                ? '会計準備中'
                : pending.some(isUnknownOperation)
                    ? '通信確認中'
                    : `保存中 ${pending.length}`;
            pendingState.classList.toggle('is-unknown', pending.some(isUnknownOperation));
        }
    };

    const syncSlipRow = (row, slip) => {
        row.dataset.slipId = String(slip.id);
        const isOpen = slip.status === 'open' && !slip.checkoutPending;
        row.classList.toggle('slip-list__row--open', isOpen);
        row.classList.toggle('slip-list__row--not-open', !isOpen);
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
        const checkoutButton = buildElement('button', 'btn btn-sm btn-primary');
        checkoutButton.type = 'button';
        checkoutButton.dataset.businessStartCheckout = '';
        const receiptButton = buildElement('button', 'btn btn-sm btn-outline-primary', '領収書');
        receiptButton.type = 'button';
        receiptButton.dataset.businessPrintReceipt = '';
        const cancelButton = buildElement('button', 'btn btn-sm btn-outline-danger', '会計取消');
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

    const reconcileUnknownOperation = async (operation) => {
        operation.state = 'unknown';
        renderProjectedSnapshot();
        const refreshed = await loadSlips();
        if (refreshed) {
            pendingOperations.delete(operation.operationId);
            renderProjectedSnapshot();
            return true;
        }
        showOperationNotice('保存結果を確認できません。通信復旧後に一覧を再取得してください。会計は同期確認後に行えます。');
        return false;
    };

    const postEditorOperation = async (operation) => {
        if (!businessSlipEditorOperationUrl) {
            pendingOperations.delete(operation.operationId);
            renderProjectedSnapshot();
            showOperationNotice('営業中の編集保存先を取得できません。');
            return false;
        }

        operation.state = 'saving';
        renderProjectedSnapshot();
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), operationTimeoutMs);
        try {
            const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
            const response = await fetch(businessSlipEditorOperationUrl, {
                method: 'POST',
                signal: controller.signal,
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest',
                    ...(token ? { RequestVerificationToken: token } : {})
                },
                body: JSON.stringify({
                    operationId: operation.operationId,
                    slipId: Number(operation.slipId),
                    operationType: operation.operationType,
                    payload: operation.payload
                })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.succeeded || !result?.snapshot) {
                pendingOperations.delete(operation.operationId);
                renderProjectedSnapshot();
                showOperationNotice(result?.message || '保存できませんでした。変更を取り消しました。');
                return false;
            }

            pendingOperations.delete(operation.operationId);
            applySnapshot(result.snapshot);
            return true;
        } catch (error) {
            if (error?.name === 'AbortError' || !navigator.onLine) {
                return reconcileUnknownOperation(operation);
            }
            pendingOperations.delete(operation.operationId);
            renderProjectedSnapshot();
            showOperationNotice('保存できませんでした。変更を取り消しました。');
            return false;
        } finally {
            window.clearTimeout(timeout);
        }
    };

    const enqueueEditorOperation = (operation) => {
        const normalized = {
            ...operation,
            operationId: operation.operationId || window.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`,
            state: 'queued'
        };
        pendingOperations.set(normalized.operationId, normalized);
        renderProjectedSnapshot();
        const lane = operationLane(normalized);
        const previous = operationLanes.get(lane) || Promise.resolve();
        const running = previous
            .catch(() => false)
            .then(() => postEditorOperation(normalized));
        const tracked = running.finally(() => {
            if (operationLanes.get(lane) === tracked) {
                operationLanes.delete(lane);
            }
        });
        operationLanes.set(lane, tracked);
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
        const running = Array.from(operationLanes.values());
        if (running.length > 0) {
            await Promise.all(running);
        }
        if (Array.from(pendingOperations.values()).some(isUnknownOperation)) {
            return false;
        }
        return loadSlips();
    };

    const markSaved = (payloadRows) => {
        karaokeDraft.markSaved(payloadRows);
        renderSlips();
    };

    const submitDraftInternal = async () => {
        const payloadRows = collectDirtyPayload();
        if (payloadRows.length === 0) {
            karaokeDraft.write();
            setKaraokeStatus('saved', '同期済み');
            return true;
        }

        isSaving = true;
        setKaraokeStatus('saving');

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: JSON.stringify(buildSavePayload(payloadRows)),
                headers: buildSaveHeaders()
            });

            const result = await response.json().catch(() => null);
            if (!response.ok || (result && result.succeeded === false)) {
                throw new Error(result?.message || 'Karaoke save failed.');
            }

            markSaved(payloadRows);
            if (collectDirtyPayload().length === 0) {
                setKaraokeStatus('saved');
            } else {
                setKaraokeStatus('dirty');
            }
            return true;
        } catch {
            karaokeDraft.write();
            setKaraokeStatus('error');
            return false;
        } finally {
            isSaving = false;
        }
    };

    const submitDraft = () => {
        if (savePromise) {
            return savePromise;
        }

        savePromise = submitDraftInternal().finally(() => {
            savePromise = null;
        });
        return savePromise;
    };

    const submitAfterFlush = async (targetForm, submitter) => {
        window.AppLoading?.show(targetForm);
        const saved = await submitDraft();
        if (!saved) {
            window.AppLoading?.hide(targetForm);
            return;
        }

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
            const key = orderGroupToggle.dataset.businessOrderGroupToggle;
            if (expandedOrderGroupKeys.has(key)) {
                expandedOrderGroupKeys.delete(key);
            } else {
                expandedOrderGroupKeys.add(key);
            }
            const row = orderGroupToggle.closest('[data-business-slip-row]');
            const slip = row ? getSlip(row.dataset.slipId) : null;
            if (row && slip) syncSlipDetails(row, slip);
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
                expandedSlipIds.add(slipId);
            }
            syncSlipDetails(row, slip);
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
        void submitDraft().finally(() => window.AppLoading?.hide(form));
    });

    document.addEventListener('submit', (event) => {
        const targetForm = event.target;
        if (!(targetForm instanceof HTMLFormElement) || targetForm === form) {
            return;
        }

        if (targetForm.dataset.karaokeFlushBypass === 'true') {
            delete targetForm.dataset.karaokeFlushBypass;
            allowNextPageUnload();
            return;
        }

        if (event.defaultPrevented || !targetForm.checkValidity() || collectDirtyPayload().length === 0) {
            return;
        }

        event.preventDefault();
        void submitAfterFlush(targetForm, event.submitter);
    }, true);

    document.addEventListener('click', (event) => {
        const anchor = event.target.closest('a[data-business-flush-karaoke]');
        if (!anchor || !shouldFlushForAnchor(anchor, event)) {
            return;
        }

        event.preventDefault();
        if (navigationInFlight) {
            return;
        }

        navigationInFlight = true;
        window.AppLoading?.show(anchor);
        void submitDraft().then((saved) => {
            if (!saved) {
                navigationInFlight = false;
                window.AppLoading?.hide(anchor);
                return;
            }
            void waitForBusinessOperations().then((synchronized) => {
                if (!synchronized) {
                    navigationInFlight = false;
                    window.AppLoading?.hide(anchor);
                    showOperationNotice('保存結果を確認できない操作があります。同期後に移動してください。');
                    return;
                }

                allowNextPageUnload();
                window.location.assign(anchor.href);
            });
        });
    });

    window.addEventListener('keydown', (event) => {
        if (event.key !== 'F5' && !(event.key.toLowerCase() === 'r' && (event.ctrlKey || event.metaKey))) {
            return;
        }

        if (hasPendingOperations()) {
            event.preventDefault();
            showOperationNotice('保存中の変更があります。同期完了後に再読み込みできます。');
            return;
        }

        event.preventDefault();
        markDirtyStatus();
    });

    window.addEventListener('beforeunload', (event) => {
        if (allowPageUnload || collectDirtyPayload().length === 0) {
            return;
        }

        event.preventDefault();
        event.returnValue = '';
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
    window.prosperBusinessHomeFlushKaraoke = submitDraft;
    window.prosperBusinessHomeEnqueueEditorOperation = enqueueEditorOperation;
    window.prosperBusinessHomeSetCheckoutLock = setCheckoutLock;
    window.prosperBusinessHomeGetPendingForSlip = pendingForSlip;
    window.prosperBusinessHomeWaitForOperations = waitForBusinessOperations;
})();
