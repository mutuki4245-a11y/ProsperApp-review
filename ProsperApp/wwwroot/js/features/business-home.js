(() => {
    const config = window.ProsperBusinessHomeConfig ?? {};
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
    const slipFilterButtons = Array.from(form.querySelectorAll('[data-business-slip-filter]'));
    const detailModalElement = document.querySelector('[data-business-slip-detail-modal]');
    const detailModalTitle = detailModalElement?.querySelector('[data-business-slip-detail-title]');
    const detailModalSubtitle = detailModalElement?.querySelector('[data-business-slip-detail-subtitle]');
    const detailModalContent = detailModalElement?.querySelector('[data-business-slip-detail-content]');
    const detailModal = detailModalElement && window.bootstrap?.Modal
        ? bootstrap.Modal.getOrCreateInstance(detailModalElement)
        : null;
    const filterCountElements = new Map(
        Array.from(form.querySelectorAll('[data-business-filter-count]'))
            .map((element) => [element.dataset.businessFilterCount, element])
    );
    const tableOptions = Array.isArray(config.tableOptions) ? config.tableOptions : [];
    const tableMetadataByDisplay = new Map(
        tableOptions.map((table, index) => [
            String(table.displayName || '').trim(),
            {
                tableCode: String(table.tableCode || '').trim(),
                categoryNo: Number(table.categoryNo) || 0,
                sortOrder: Number.isFinite(Number(table.sortOrder)) ? Number(table.sortOrder) : index
            }
        ])
    );
    const tableCollator = new Intl.Collator('ja', { numeric: true, sensitivity: 'base' });
    const businessSlipsUrl = config.businessSlipsUrl || '';
    const flushBusinessHomeChangesUrl = config.flushBusinessHomeChangesUrl || '';
    const refreshIntervalMs = 10000;
    const elapsedRefreshIntervalMs = 15000;
    const accountingUnit = 240;
    let slips = [];
    let serverSnapshot = null;
    let snapshotRevision = -1;
    const pendingOperations = new Map();
    const checkoutLockSlipIds = new Set();
    const expandedOrderGroupKeys = new Set();
    let activeDetailSlipId = null;
    let hasLoaded = false;
    let refreshPromise = null;
    let isSaving = false;
    let flushPromise = null;
    let pendingFlushBatch = null;
    let flushTimer = null;
    let navigationInFlight = false;
    let activeSlipFilter = 'all';

    const formatYen = window.MoneyText.yen;
    const formatSignedYen = (value) => {
        const amount = Math.round(Number(value) || 0);
        return amount < 0 ? `-${formatYen(Math.abs(amount))}` : formatYen(amount);
    };
    const toQuantity = (value) => Math.max(0, Math.trunc(Number(value) || 0));
    const defaultCustomerName = (lineNo) => `ご新規様${lineNo || ''}`;
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
        const names = customers.map((item) => item.displayName || defaultCustomerName(item.lineNo)).filter(Boolean);
        const castNames = [];
        nominations.forEach((item) => {
            if (item.displayName && !castNames.includes(item.displayName)) castNames.push(item.displayName);
        });
        slip.customerCount = customers.filter((item) => item.status === 'active').length;
        slip.customerNames = names.join('、') || 'お客様名なし';
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
                displayName: label || defaultCustomerName(lineNo),
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
                customer.displayName = label || defaultCustomerName(customer.lineNo);
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
    const buildActionIcon = (iconName) => {
        const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        icon.classList.add('business-slip-card__action-icon');
        icon.setAttribute('aria-hidden', 'true');
        icon.setAttribute('focusable', 'false');
        const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
        use.setAttribute('href', `/icons/lucide-actions.svg#${iconName}`);
        icon.appendChild(use);
        return icon;
    };
    const buildActionButton = (className, label, iconName) => {
        const button = buildElement('button', `${className} business-slip-card__action-button`);
        button.type = 'button';
        const labelElement = buildElement('span', 'business-slip-card__action-label', label);
        labelElement.dataset.businessActionLabel = '';
        button.append(buildActionIcon(iconName), labelElement);
        return button;
    };
    const slipTableMetadata = (slip) => tableMetadataByDisplay.get(String(slip?.tableDisplay || '').trim()) || {
        tableCode: String(slip?.tableDisplay || '').trim(),
        categoryNo: 0,
        sortOrder: Number.MAX_SAFE_INTEGER
    };
    const compareSlipsByTable = (left, right) => {
        const leftTable = slipTableMetadata(left);
        const rightTable = slipTableMetadata(right);
        return leftTable.categoryNo - rightTable.categoryNo
            || leftTable.sortOrder - rightTable.sortOrder
            || tableCollator.compare(String(left.tableDisplay || ''), String(right.tableDisplay || ''))
            || (Number(left.slipNo) || Number(left.id) || 0) - (Number(right.slipNo) || Number(right.id) || 0);
    };
    const isCheckoutReady = (slip) => slip?.status === 'checkout_ready' || Boolean(slip?.checkoutPending);
    const isOpenSlip = (slip) => ['open', 'checkout_ready'].includes(slip?.status);
    const compactSlipNumber = (slip) => {
        const value = String(slip?.slipNo || slip?.id || '');
        const segments = value.split('-');
        return segments.length > 2 ? segments.slice(-2).join('-') : value;
    };
    const tableCategoryLabel = (slip) => {
        const table = slipTableMetadata(slip);
        const prefix = table.tableCode.match(/^[^\d\s]+/)?.[0];
        return prefix
            ? `${prefix}カテゴリ`
            : table.categoryNo > 0 ? `卓番カテゴリ ${table.categoryNo}` : '卓番カテゴリ';
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

    const karaokeState = (slip) => {
        const initialQuantity = getInitialQuantity(slip);
        const editable = slip.status === 'open' && !slip.checkoutPending;
        const displayQuantity = editable ? getDisplayQuantity(slip) : initialQuantity;
        const baseAmount = Number(slip.accountingAmount || 0) - initialQuantity * accountingUnit;
        return {
            editable,
            initialQuantity,
            displayQuantity,
            baseAmount,
            accountingAmount: baseAmount + displayQuantity * accountingUnit,
            dirty: displayQuantity !== initialQuantity
        };
    };

    const syncSlipAccountingAmount = (row, slip) => {
        const amountValue = row.querySelector('[data-business-slip-amount-value]');
        const state = karaokeState(slip);
        setText(amountValue, formatYen(state.accountingAmount));
    };

    const latestCustomerLeftAt = (slip) => {
        const leftTimes = (Array.isArray(slip?.customers) ? slip.customers : [])
            .map((customer) => Date.parse(customer.leftAt))
            .filter(Number.isFinite);
        return leftTimes.length > 0 ? Math.max(...leftTimes) : null;
    };

    const elapsedMinutes = (slip, now = Date.now()) => {
        if (!['open', 'checkout_ready', 'checked_out'].includes(slip?.status)) {
            return null;
        }

        const openedAt = Date.parse(slip.openedAt);
        if (!Number.isFinite(openedAt)) {
            return null;
        }

        const closedAt = Date.parse(slip.closedAt);
        const recordedEndAt = Number.isFinite(closedAt) ? closedAt : latestCustomerLeftAt(slip);
        const isActivelySeated = slip.status === 'open' && !slip.checkoutPending;
        const endAt = isActivelySeated || recordedEndAt === null ? now : Math.min(now, recordedEndAt);
        return Math.max(0, Math.floor((endAt - openedAt) / 60000));
    };

    const formatElapsedMinutes = (minutes) => {
        const normalized = Math.max(0, Math.trunc(Number(minutes) || 0));
        return `${Math.floor(normalized / 60)}時間${normalized % 60}分`;
    };

    const syncElapsedTime = (row, slip, now = Date.now()) => {
        const elapsed = row.querySelector('[data-business-slip-elapsed]');
        if (!elapsed) {
            return;
        }

        const minutes = elapsedMinutes(slip, now);
        elapsed.hidden = minutes === null;
        if (minutes !== null) {
            setText(elapsed, `在席 ${formatElapsedMinutes(minutes)}`);
        }
    };

    const syncElapsedTimes = () => {
        const now = Date.now();
        list.querySelectorAll('[data-business-slip-row]').forEach((row) => {
            const slip = getSlip(row.dataset.slipId);
            if (slip) {
                syncElapsedTime(row, slip, now);
            }
        });
    };

    const buildCardPerson = (text, kind, departed = false) => {
        const person = buildElement('span', `business-slip-card__person business-slip-card__person--${kind}`, text);
        person.title = text;
        person.classList.toggle('is-departed', departed);
        return person;
    };

    const syncCardPeople = (row, slip) => {
        const customerList = row.querySelector('[data-business-slip-customers]');
        const nominationList = row.querySelector('[data-business-slip-casts]');
        if (customerList) {
            const customerPanels = (Array.isArray(slip.customers) ? slip.customers : [])
                .filter((customer) => customer.status !== 'cancelled')
                .map((customer) => buildCardPerson(
                    customer.displayName || defaultCustomerName(customer.lineNo),
                    'customer',
                    customer.status !== 'active'
                ));
            customerList.replaceChildren(...customerPanels);
            customerList.hidden = customerPanels.length === 0;
        }
        if (nominationList) {
            const nominationPanels = (Array.isArray(slip.nominations) ? slip.nominations : [])
                .filter((nomination) => nomination.status === 'active')
                .map((nomination) => {
                    const castName = nomination.displayName || 'キャスト';
                    const kind = nomination.nominationDisplayName || nomination.nominationKind || '指名';
                    return buildCardPerson(`${castName} — ${kind}`, 'nomination');
                });
            nominationList.replaceChildren(...nominationPanels);
            nominationList.hidden = nominationPanels.length === 0;
        }
    };

    const buildKaraokeEditor = (slip) => {
        const state = karaokeState(slip);
        const section = buildElement('section', 'business-slip-detail-karaoke');
        section.append(
            buildElement('strong', 'business-slip-detail-karaoke__label', 'カラオケ'),
            buildElement('span', 'business-slip-detail-karaoke__amount', `会計額 ${formatYen(state.accountingAmount)}`)
        );

        if (!state.editable) {
            section.appendChild(buildElement('span', 'business-slip-detail-karaoke__readonly', `${state.displayQuantity}回`));
            return section;
        }

        const control = buildElement('div', 'slip-list__karaoke business-slip-detail-karaoke__control');
        control.dataset.businessKaraokeRow = '';
        control.dataset.slipId = String(slip.id);
        control.dataset.initialQuantity = String(state.initialQuantity);
        control.dataset.quantity = String(state.displayQuantity);
        control.dataset.accountingBaseAmount = String(state.baseAmount);
        control.dataset.karaokeAccountingUnit = String(accountingUnit);
        control.classList.toggle('is-dirty', state.dirty);

        const decrement = buildElement('button', 'btn btn-outline-secondary', '-');
        decrement.type = 'button';
        decrement.setAttribute('aria-label', 'カラオケを減らす');
        decrement.disabled = state.displayQuantity <= 0;
        decrement.dataset.businessKaraokeDecrement = '';
        const increment = buildElement('button', 'btn btn-outline-primary', '+');
        increment.type = 'button';
        increment.setAttribute('aria-label', 'カラオケを増やす');
        increment.dataset.businessKaraokeIncrement = '';
        const quantity = buildElement('strong', null, state.displayQuantity);
        quantity.dataset.businessKaraokeDisplay = '';
        control.append(decrement, quantity, increment);
        section.appendChild(control);
        return section;
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
            content.appendChild(buildElement('strong', 'business-slip-detail-summary__empty', '在席中のお客様なし'));
        } else {
            customers.forEach((customer) => {
                const chip = buildElement('strong', 'business-slip-detail-summary__chip', customer.displayName || defaultCustomerName(customer.lineNo));
                if (customer.status !== 'active') chip.classList.add('is-departed');
                content.appendChild(chip);
            });
        }
        return detailSummary('お客様', 'customers', content, 'お客様を編集');
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

    const orderDetailType = (line) => {
        if (line?.itemType === 'set_fee') return 'set_fee';
        if (line?.itemType === 'extension_fee') return 'extension_fee';
        if (line?.itemType === 'nomination_fee' || line?.sourceType === 'nomination_fee') return 'nomination_fee';
        if (line?.itemType === 'karaoke') return 'karaoke';
        return 'other';
    };

    const orderDetailRank = (line) => ({
        set_fee: 0,
        extension_fee: 1,
        nomination_fee: 2,
        karaoke: 3,
        other: 4
    }[orderDetailType(line)]);

    const renderOrders = (target, slip) => {
        target.replaceChildren();
        const orders = (Array.isArray(slip.orders) ? slip.orders : [])
            .filter((line) => line.status === 'active')
            .sort((left, right) =>
                orderDetailRank(left) - orderDetailRank(right) ||
                String(left.orderedAt || left.orderedTime || '').localeCompare(String(right.orderedAt || right.orderedTime || '')) ||
                (Number(left.lineNo) || 0) - (Number(right.lineNo) || 0));
        if (orders.length === 0) {
            target.appendChild(buildElement('span', 'business-slip-detail-summary__empty', '注文はありません。'));
            return;
        }

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

        renderGroups(orders, 'order', (line) => `${orderDetailType(line)}\u0000${line.itemName || ''}\u0000${Number(line.unitPrice) || 0}\u0000${line.backCastId || ''}`);
    };

    const renderAdjustments = (target, slip) => {
        const adjustments = Array.isArray(slip.adjustments) ? slip.adjustments.filter((item) => item.status === 'active') : [];
        adjustments.forEach((adjustment) => {
            target.append(detailLine(adjustment.lineName || '-', adjustment.createdTime || '-', formatSignedYen(adjustment.amount), adjustment.pending));
        });
    };

    const buildAccountingTotals = (slip) => {
        const karaoke = karaokeState(slip);
        const karaokeNetUnit = Math.round(accountingUnit / 1.2);
        const orderSubtotal = Math.round((Number(slip.orderSubtotalAmount) || 0) + (karaoke.displayQuantity - karaoke.initialQuantity) * karaokeNetUnit);
        const pricingSubtotal = Math.round(Number(slip.pricingSubtotalAmount) || 0);
        const subtotal = orderSubtotal + pricingSubtotal;
        const serviceCharge = Math.round(subtotal * 0.20);
        const total = Math.round(karaoke.accountingAmount);
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

    const syncEditorButtons = (container, slip) => {
        const canEdit = slip.status === 'open' && !slip.checkoutPending;
        container.querySelectorAll('[data-business-slip-editor]').forEach((button) => {
            button.hidden = !canEdit;
            if (canEdit) {
                button.dataset.businessSlipId = String(slip.id);
            } else {
                delete button.dataset.businessSlipId;
            }
        });
    };

    const syncPendingState = (row, slip) => {
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

    const detailHeroItem = (label, value) => {
        const item = buildElement('div');
        item.append(buildElement('span', null, label), buildElement('strong', null, value));
        return item;
    };

    const buildSlipDetailContent = (slip) => {
        const wrapper = buildElement('div', 'business-slip-detail-modal');
        const stateLabel = slip.checkoutPending ? '会計準備中' : slip.statusDisplay;
        const hero = buildElement('section', 'slip-detail-hero business-slip-detail-modal__hero');
        const heroItems = buildElement('div', 'slip-detail-hero__items');
        heroItems.append(
            detailHeroItem('卓番', slip.tableDisplay || '-'),
            detailHeroItem('入店', slip.openedTime || '-'),
            detailHeroItem('お客様数', `${Number(slip.customerCount) || 0}名`),
            detailHeroItem('状態', stateLabel),
            detailHeroItem('会計額', formatYen(karaokeState(slip).accountingAmount))
        );
        hero.appendChild(heroItems);

        const layout = buildElement('div', 'business-slip-detail-layout');
        const activity = buildElement('div', 'business-slip-detail-layout__activity');
        const accounting = buildElement('aside', 'business-slip-detail-layout__accounting');
        accounting.setAttribute('aria-label', '注文と会計');
        activity.append(customerSummary(slip), nominationSummary(slip));
        accounting.append(buildKaraokeEditor(slip), orderSection(slip), buildAccountingTotals(slip));
        layout.append(activity, accounting);
        wrapper.append(hero, layout);
        syncEditorButtons(wrapper, slip);
        return wrapper;
    };

    const syncDetailModal = () => {
        if (!activeDetailSlipId || !detailModalContent) {
            return;
        }

        const slip = getSlip(activeDetailSlipId);
        if (!slip) {
            detailModal?.hide();
            return;
        }

        const stateLabel = slip.checkoutPending ? '会計準備中' : slip.statusDisplay;
        setText(detailModalTitle, `卓 ${slip.tableDisplay || '-'}`);
        setText(detailModalSubtitle, `${stateLabel} / 伝票 #${compactSlipNumber(slip)}`);
        detailModalContent.replaceChildren(buildSlipDetailContent(slip));
    };

    const openSlipDetail = (slipId) => {
        if (!detailModal || !detailModalContent) {
            return;
        }

        activeDetailSlipId = String(slipId || '');
        syncDetailModal();
        detailModal.show();
    };

    const syncSlipRow = (row, slip) => {
        row.dataset.slipId = String(slip.id);
        const isOpen = slip.status === 'open' && !slip.checkoutPending;
        row.classList.toggle('slip-list__row--open', isOpen);
        row.classList.toggle('slip-list__row--not-open', !isOpen);
        row.classList.toggle('slip-list__row--checkout-ready', slip.status === 'checkout_ready' || Boolean(slip.checkoutPending));
        row.classList.toggle('slip-list__row--checked-out', slip.status === 'checked_out');
        row.setAttribute('aria-label', `${slip.tableDisplay} ${slip.checkoutPending ? '会計準備中' : slip.statusDisplay}`);
        row.querySelectorAll('[data-business-slip-open-detail]').forEach((target) => {
            target.dataset.businessSlipOpenDetail = String(slip.id);
            target.setAttribute('aria-label', `卓 ${slip.tableDisplay || '-'} の詳細を開く`);
        });

        setText(row.querySelector('[data-business-slip-table]'), slip.tableDisplay);
        setText(row.querySelector('[data-business-slip-time]'), slip.openedTime);
        syncCardPeople(row, slip);
        setText(row.querySelector('[data-business-slip-number]'), `伝票 #${compactSlipNumber(slip)}`);
        setText(row.querySelector('[data-business-slip-status]'), slip.checkoutPending ? '会計準備中' : slip.statusDisplay);
        setText(row.querySelector('[data-business-slip-customer-count]'), `${Number(slip.customerCount) || 0}名`);
        syncElapsedTime(row, slip);
        const checkoutButton = row.querySelector('[data-business-start-checkout]');
        const receiptButton = row.querySelector('[data-business-print-receipt]');
        const cancelButton = row.querySelector('[data-business-cancel-checkout]');
        if (checkoutButton) {
            checkoutButton.hidden = !['open', 'checkout_ready'].includes(slip.status);
            checkoutButton.dataset.businessStartCheckout = String(slip.id);
            checkoutButton.disabled = Boolean(slip.checkoutPending);
            setText(checkoutButton.querySelector('[data-business-action-label]'), slip.checkoutPending
                ? '会計準備中'
                : slip.status === 'checkout_ready' ? '決済' : '会計');
        }
        if (receiptButton) {
            receiptButton.hidden = slip.status !== 'checked_out';
            receiptButton.dataset.businessPrintReceipt = String(slip.id);
        }
        if (cancelButton) {
            cancelButton.hidden = slip.status !== 'checked_out';
            cancelButton.dataset.businessCancelCheckout = String(slip.id);
        }
        syncSlipAccountingAmount(row, slip);
        syncEditorButtons(row, slip);
        syncPendingState(row, slip);
    };

    const buildSlipRow = (slip) => {
        const row = buildElement('article', 'slip-list__row slip-list__row--action business-slip-card');
        row.dataset.businessSlipRow = '';
        const header = buildElement('header', 'business-slip-card__header');
        const identity = buildElement('div', 'business-slip-card__identity');
        const table = buildElement('strong', 'slip-list__table');
        table.dataset.businessSlipTable = '';
        const slipNumber = buildElement('span', 'business-slip-card__number');
        slipNumber.dataset.businessSlipNumber = '';
        identity.append(table, slipNumber);
        const headerActions = buildElement('div', 'business-slip-card__header-actions');
        const syncState = buildElement('span', 'slip-list__sync-state');
        syncState.dataset.businessSlipSyncState = '';
        syncState.hidden = true;
        const statusBadge = buildElement('span', 'business-slip-card__status');
        statusBadge.dataset.businessSlipStatus = '';
        headerActions.append(syncState, statusBadge);
        header.dataset.businessSlipOpenDetail = '';
        header.append(identity, headerActions);

        const timing = buildElement('div', 'business-slip-card__timing');
        timing.dataset.businessSlipOpenDetail = '';
        timing.append(buildElement('span', null, '入店'));
        const openedTime = buildElement('span', 'slip-list__time');
        openedTime.dataset.businessSlipTime = '';
        const customerCount = buildElement('strong');
        customerCount.dataset.businessSlipCustomerCount = '';
        const elapsed = buildElement('strong', 'business-slip-card__elapsed');
        elapsed.dataset.businessSlipElapsed = '';
        elapsed.hidden = true;
        timing.append(openedTime, buildElement('span', null, '・'), customerCount, elapsed);

        const main = buildElement('div', 'slip-list__row-main business-slip-card__body');
        main.dataset.businessSlipOpenDetail = '';
        main.tabIndex = 0;
        main.setAttribute('role', 'button');
        const people = buildElement('div', 'business-slip-card__people');
        const customers = buildElement('div', 'business-slip-card__person-list business-slip-card__person-list--customers');
        customers.dataset.businessSlipCustomers = '';
        const casts = buildElement('div', 'business-slip-card__person-list business-slip-card__person-list--nominations');
        casts.dataset.businessSlipCasts = '';
        people.append(customers, casts);

        main.append(people, buildAmountElement(slip));
        const actions = buildElement('div', 'slip-list__actions');
        const actionButtons = buildElement('div', 'slip-list__action-buttons');
        const customerButton = buildActionButton('btn business-slip-card__quick-action', 'お客様', 'user-round');
        customerButton.dataset.businessSlipEditor = 'customers';
        const nominationButton = buildActionButton('btn business-slip-card__quick-action', '指名', 'star');
        nominationButton.dataset.businessSlipEditor = 'nominations';
        const orderButton = buildActionButton('btn business-slip-card__quick-action', '注文', 'clipboard-list');
        orderButton.dataset.businessSlipEditor = 'orders';
        const checkoutButton = buildActionButton('btn btn-sm btn-primary slip-list__checkout-action', '会計', 'badge-japanese-yen');
        checkoutButton.dataset.businessStartCheckout = '';
        const receiptButton = buildElement('button', 'btn btn-sm btn-outline-primary', '領収書');
        receiptButton.type = 'button';
        receiptButton.dataset.businessPrintReceipt = '';
        const cancelButton = buildElement('button', 'btn btn-sm btn-outline-danger slip-list__checkout-action', '会計取消');
        cancelButton.type = 'button';
        cancelButton.dataset.businessCancelCheckout = '';
        actionButtons.append(customerButton, nominationButton, orderButton, checkoutButton, receiptButton, cancelButton);
        actions.appendChild(actionButtons);
        row.append(header, timing, main, actions);

        syncSlipRow(row, slip);

        return row;
    };

    const updateSlipFilters = (openSlips, paidSlips) => {
        const openCount = openSlips.filter((slip) => !isCheckoutReady(slip)).length;
        const checkoutReadyCount = openSlips.filter(isCheckoutReady).length;
        setText(filterCountElements.get('all'), openSlips.length + paidSlips.length);
        setText(filterCountElements.get('open'), openCount);
        setText(filterCountElements.get('checkout_ready'), checkoutReadyCount);
        setText(filterCountElements.get('checked_out'), paidSlips.length);
        slipFilterButtons.forEach((button) => {
            const isActive = button.dataset.businessSlipFilter === activeSlipFilter;
            button.classList.toggle('is-active', isActive);
            button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
        });
    };

    const buildCategorySection = (categorySlips, rowsBySlipId, renderedSlipIds) => {
        const firstSlip = categorySlips[0];
        const category = buildElement('section', 'business-slip-category');
        const divider = buildElement('div', 'business-slip-category__divider');
        divider.append(
            buildElement('span'),
            buildElement('strong', null, tableCategoryLabel(firstSlip)),
            buildElement('small', null, `${categorySlips.length}伝票`),
            buildElement('span')
        );
        const grid = buildElement('div', 'business-slip-grid');
        categorySlips.forEach((slip) => {
            const slipId = String(slip.id);
            const existingRow = rowsBySlipId.get(slipId);
            const row = existingRow ?? buildSlipRow(slip);
            if (existingRow) syncSlipRow(row, slip);
            renderedSlipIds.add(slipId);
            grid.appendChild(row);
        });
        category.append(divider, grid);
        return category;
    };

    const buildPaidSection = (paidSlips, rowsBySlipId, renderedSlipIds, wasExpanded, forceExpanded = false) => {
        const paidTotal = paidSlips.reduce((sum, slip) => sum + (Number(slip.accountingAmount) || 0), 0);
        const details = buildElement('details', 'business-slip-paid');
        details.dataset.businessPaidSlips = '';
        details.open = forceExpanded || wasExpanded;
        const summary = buildElement('summary');
        summary.append(
            buildElement('span', 'business-slip-paid__chevron', '⌄'),
            buildElement('strong', null, '会計済み'),
            buildElement('span', 'business-slip-paid__count', `${paidSlips.length}件`),
            buildElement('small', null, formatYen(paidTotal))
        );
        const body = buildElement('div', 'business-slip-paid__body');
        if (paidSlips.length === 0) {
            body.appendChild(buildElement('p', 'business-slip-paid__empty', '会計済みの伝票はありません。'));
        } else {
            paidSlips.forEach((slip) => {
                const slipId = String(slip.id);
                const existingRow = rowsBySlipId.get(slipId);
                const row = existingRow ?? buildSlipRow(slip);
                if (existingRow) syncSlipRow(row, slip);
                renderedSlipIds.add(slipId);
                body.appendChild(row);
            });
        }
        details.append(summary, body);
        return details;
    };

    const renderSlips = () => {
        cleanupDraft();
        const rowsBySlipId = new Map(
            Array.from(list.querySelectorAll('[data-business-slip-row]'))
                .map((row) => [row.dataset.slipId, row])
        );
        const paidExpanded = list.querySelector('[data-business-paid-slips]')?.open ?? false;
        const openSlips = slips.filter((slip) => isOpenSlip(slip)).sort(compareSlipsByTable);
        const paidSlips = slips.filter((slip) => slip.status === 'checked_out').sort(compareSlipsByTable);
        updateSlipFilters(openSlips, paidSlips);
        const filteredOpenSlips = activeSlipFilter === 'checked_out'
            ? []
            : openSlips.filter((slip) =>
                activeSlipFilter === 'all'
                || (activeSlipFilter === 'checkout_ready' ? isCheckoutReady(slip) : !isCheckoutReady(slip))
            );
        const showPaidSlips = activeSlipFilter === 'all' || activeSlipFilter === 'checked_out';
        const renderedSlipIds = new Set();
        const content = document.createDocumentFragment();
        const hasVisibleSlips = filteredOpenSlips.length > 0 || (showPaidSlips && paidSlips.length > 0);
        if (!hasVisibleSlips) {
            const empty = buildElement('div', 'business-slip-board__empty');
            if (activeSlipFilter === 'checked_out') {
                empty.append(
                    buildElement('strong', null, '会計済みの伝票はありません'),
                    buildElement('span', null, '会計が完了すると、ここに表示されます。')
                );
            } else if (openSlips.length === 0) {
                empty.append(
                    buildElement('strong', null, '未会計の伝票はありません'),
                    buildElement('span', null, '新しい伝票を追加すると、卓番カテゴリごとに表示されます。')
                );
            } else {
                empty.append(
                    buildElement('strong', null, '該当する伝票はありません'),
                    buildElement('span', null, '別の絞り込みを選択してください。')
                );
            }
            content.appendChild(empty);
        } else if (filteredOpenSlips.length > 0) {
            const categories = new Map();
            filteredOpenSlips.forEach((slip) => {
                const categoryNo = slipTableMetadata(slip).categoryNo;
                const categorySlips = categories.get(categoryNo) || [];
                categorySlips.push(slip);
                categories.set(categoryNo, categorySlips);
            });
            categories.forEach((categorySlips) => {
                content.appendChild(buildCategorySection(categorySlips, rowsBySlipId, renderedSlipIds));
            });
        }
        if (showPaidSlips && paidSlips.length > 0) {
            content.appendChild(buildPaidSection(
                paidSlips,
                rowsBySlipId,
                renderedSlipIds,
                paidExpanded,
                activeSlipFilter === 'checked_out'
            ));
        }
        list.replaceChildren(content);
        if (revealButton) {
            revealButton.hidden = openSlips.length + paidSlips.length === 0;
        }
        syncDetailModal();
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
        if (raw.includes('store_slip_customer_not_found')) return '対象のお客様を確認してください。';
        if (raw.includes('store_slip_nomination_not_found')) return '対象の指名を確認してください。';
        if (raw.includes('store_slip_adjustment_not_found')) return '対象の自由明細を確認してください。';
        if (raw.includes('store_order_line_not_found')) return '対象の注文を確認してください。';
        if (raw.includes('invalid_customer_label')) return 'お客様名は100文字以内で入力してください。';
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

    const changeKaraokeQuantity = (target, delta) => {
        const row = target.closest('[data-business-karaoke-row]');
        if (!row) {
            return false;
        }

        const slip = getSlip(row.dataset.slipId);
        if (!slip || slip.status !== 'open' || slip.checkoutPending) {
            return false;
        }

        const nextQuantity = toQuantity(row.dataset.quantity) + delta;
        karaokeDraft.setQuantity(slip, nextQuantity);
        renderProjectedSnapshot();
        markDirtyStatus();
        return true;
    };

    form.addEventListener('click', (event) => {
        const filterButton = event.target.closest('[data-business-slip-filter]');
        if (filterButton) {
            activeSlipFilter = filterButton.dataset.businessSlipFilter || 'all';
            renderSlips();
            return;
        }

        const detailTarget = event.target.closest('[data-business-slip-open-detail]');
        if (detailTarget && form.contains(detailTarget)) {
            if (event.target.closest('button, a, input, select, textarea, label, summary')) {
                return;
            }
            const row = detailTarget.closest('[data-business-slip-row]');
            if (row?.dataset.slipId) {
                openSlipDetail(row.dataset.slipId);
            }
            return;
        }

    });

    form.addEventListener('keydown', (event) => {
        if (event.key !== 'Enter' && event.key !== ' ') {
            return;
        }

        const detailTarget = event.target.closest('[data-business-slip-open-detail]');
        if (!detailTarget || !form.contains(detailTarget)) {
            return;
        }

        event.preventDefault();
        const row = detailTarget.closest('[data-business-slip-row]');
        if (row?.dataset.slipId) {
            openSlipDetail(row.dataset.slipId);
        }
    });

    detailModalElement?.addEventListener('click', (event) => {
        const editorButton = event.target.closest('[data-business-slip-editor]');
        if (editorButton && detailModalElement.contains(editorButton)) {
            event.preventDefault();
            event.stopPropagation();
            const section = editorButton.dataset.businessSlipEditor;
            const slipId = editorButton.dataset.businessSlipId;
            const openEditor = () => window.ProsperBusinessSlipEditor?.open?.(section, slipId);
            if (detailModalElement.classList.contains('show')) {
                detailModalElement.addEventListener('hidden.bs.modal', openEditor, { once: true });
                detailModal?.hide();
            } else {
                openEditor();
            }
            return;
        }

        const orderGroupToggle = event.target.closest('[data-business-order-group-toggle]');
        if (orderGroupToggle && detailModalElement.contains(orderGroupToggle)) {
            event.preventDefault();
            const groupKey = orderGroupToggle.dataset.businessOrderGroupToggle;
            if (expandedOrderGroupKeys.has(groupKey)) {
                expandedOrderGroupKeys.delete(groupKey);
            } else {
                expandedOrderGroupKeys.add(groupKey);
            }
            syncDetailModal();
            return;
        }

        const decrement = event.target.closest('[data-business-karaoke-decrement]');
        const increment = event.target.closest('[data-business-karaoke-increment]');
        if ((decrement || increment) && detailModalElement.contains(event.target)) {
            event.preventDefault();
            if (changeKaraokeQuantity(event.target, increment ? 1 : -1)) {
                syncDetailModal();
            }
        }
    });

    detailModalElement?.addEventListener('hidden.bs.modal', () => {
        activeDetailSlipId = null;
        expandedOrderGroupKeys.clear();
        detailModalContent?.replaceChildren(buildElement('div', 'slip-create__panel slip-detail-panel-loading', '詳細を読み込み中です。'));
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
    window.setInterval(() => {
        if (document.visibilityState === 'visible') {
            syncElapsedTimes();
        }
    }, elapsedRefreshIntervalMs);

    window.ProsperBusinessHome = Object.freeze({
        reload: loadSlips,
        flush: flushBusinessHomeChanges,
        enqueueEditorOperation,
        setCheckoutLock,
        getPendingForSlip: pendingForSlip,
        waitForOperations: waitForBusinessOperations,
        getSlip
    });
    void loadSlips();
})();
