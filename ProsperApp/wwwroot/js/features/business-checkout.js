(() => {
    const config = window.ProsperBusinessHomeConfig ?? {};
    const root = document.querySelector('[data-business-checkout-modal]');
    const sourceForm = document.querySelector('[data-business-karaoke-form]');
    if (!root || !sourceForm) return;

    const modalElement = document.getElementById('businessCheckoutModal');
    const modal = window.bootstrap?.Modal.getOrCreateInstance(modalElement);
    const paymentModalElement = document.getElementById('businessPaymentModal');
    const paymentModal = window.bootstrap?.Modal.getOrCreateInstance(paymentModalElement);
    const paymentRoot = paymentModalElement?.querySelector('[data-business-payment-modal]');
    if (!paymentRoot) return;
    const message = root.querySelector('[data-business-checkout-message]');
    const table = root.querySelector('[data-business-checkout-table]');
    const issuePanel = root.querySelector('[data-business-checkout-issue-panel]');
    const closedTime = root.querySelector('[data-business-checkout-closed-time]');
    const statement = root.querySelector('[data-business-checkout-statement]');
    const printPanel = root.querySelector('[data-business-statement-print-panel]');
    const printState = root.querySelector('[data-business-statement-print-state]');
    const paymentMessage = paymentRoot.querySelector('[data-business-payment-message]');
    const paymentTable = paymentRoot.querySelector('[data-business-payment-table]');
    const paymentDetail = paymentRoot.querySelector('[data-business-payment-detail]');
    const paymentForm = paymentRoot.querySelector('[data-business-payment-form]');
    const paymentRows = paymentRoot.querySelector('[data-business-payment-rows]');
    const paymentSummary = paymentRoot.querySelector('[data-business-payment-summary]');
    const receivedRow = paymentRoot.querySelector('[data-business-received-row]');
    const receivedAmount = paymentRoot.querySelector('[data-business-received-amount]');
    const addressee = paymentRoot.querySelector('[data-business-receipt-addressee]');
    const confirmButton = paymentRoot.querySelector('[data-business-confirm-checkout]');
    const issueAction = root.querySelector('[data-business-checkout-issue-action]');
    const printActions = root.querySelector('[data-business-checkout-print-actions]');
    const printStatementButton = root.querySelector('[data-business-print-statement]');
    const releaseCheckoutButton = root.querySelector('[data-business-release-checkout]');
    const proceedPaymentButton = root.querySelector('[data-business-proceed-payment]');
    const storagePrefix = 'prosper:checkout-statement:v1:';
    const receiptStoragePrefix = 'prosper:checkout-receipt:v2:';
    const saveResponse = window.ProsperSaveResponse;
    const fallbackPaymentMethods = [
        { methodCode: 'cash', methodName: '現金', requiresReceivedAmount: true },
        { methodCode: 'cat', methodName: 'クレジット', requiresReceivedAmount: false },
        { methodCode: 'paypay', methodName: 'PAYPAY', requiresReceivedAmount: false }
    ];
    const configuredPaymentMethods = Array.isArray(config.paymentMethods)
        ? config.paymentMethods.filter((method) => method?.methodCode && method?.methodName)
        : [];
    const paymentMethods = configuredPaymentMethods.length > 0
        ? configuredPaymentMethods
        : fallbackPaymentMethods;
    const paymentMethodByCode = new Map(
        paymentMethods.map((method) => [String(method.methodCode), method]));
    let current = null;
    let isActionInFlight = false;
    let paymentModalCloseIsProgrammatic = false;
    let modalTransition = null;
    const mutationStorageKey = `prosper:checkout-mutations:v2:${config.departmentId || 'current'}`;
    const readPendingMutations = () => {
        try {
            const rows = JSON.parse(sessionStorage.getItem(mutationStorageKey) || '[]');
            return Array.isArray(rows) ? rows : [];
        } catch {
            return [];
        }
    };
    const pendingMutationCommands = new Map(readPendingMutations()
        .filter((row) => Array.isArray(row) && row.length === 2 && row[1]?.operationId));
    const persistPendingMutations = () => {
        try {
            if (pendingMutationCommands.size) {
                sessionStorage.setItem(mutationStorageKey, JSON.stringify([...pendingMutationCommands]));
            } else {
                sessionStorage.removeItem(mutationStorageKey);
            }
        } catch {
            // In-memory commands still preserve operation IDs for this page lifetime.
        }
    };
    const deletePendingMutation = (commandKey) => {
        pendingMutationCommands.delete(commandKey);
        persistPendingMutations();
        syncPaymentMutationLock();
    };

    const syncPaymentMutationLock = () => {
        const locked = Boolean(current?.slipId && pendingMutationCommands.has(`confirm:${current.slipId}`));
        paymentRows?.querySelectorAll('[data-payment-code]').forEach((button) => {
            button.disabled = locked;
        });
        paymentRows?.querySelectorAll('[data-payment-amount]').forEach((input) => {
            const selector = `[data-payment-code="${input.dataset.paymentAmount}"]`;
            const selected = paymentRows.querySelector(selector)?.classList.contains('btn-primary') === true;
            input.disabled = locked || !selected;
        });
        if (receivedAmount) receivedAmount.disabled = locked;
        if (addressee) addressee.disabled = locked;
    };

    const businessHome = () => window.ProsperBusinessHome;
    const yen = (value) => `${Math.round(Number(value) || 0).toLocaleString('ja-JP')}円`;
    const token = () => sourceForm.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const showDialogAlert = async (value) => {
        window.AppLoading?.hide();
        if (window.AppConfirm?.alert) {
            await window.AppConfirm.alert(value);
            return;
        }

        window.alert(value);
    };
    const showDialogConfirm = async (value) => {
        window.AppLoading?.hide();
        if (window.AppConfirm?.confirm) {
            return window.AppConfirm.confirm(value);
        }

        return window.confirm(value);
    };
    const setMessage = (value = '') => {
        message.hidden = !value;
        message.textContent = value;
    };
    const setPaymentMessage = (value = '') => {
        paymentMessage.hidden = !value;
        paymentMessage.textContent = value;
    };
    const key = (slipId) => `${storagePrefix}${slipId}`;
    const readQueue = (slipId) => {
        try {
            const value = JSON.parse(localStorage.getItem(key(slipId)) || 'null');
            if (!value?.printData || !value?.state) return null;
            if (value.state === 'staff_complete') value.state = 'printed';
            return value;
        } catch {
            localStorage.removeItem(key(slipId));
            return null;
        }
    };
    const writeQueueFor = (checkout) => {
        if (!checkout?.slipId || !checkout?.queue || checkout.queueDiscarded) return;
        localStorage.setItem(key(checkout.slipId), JSON.stringify(checkout.queue));
    };
    const writeQueue = () => writeQueueFor(current);
    const removeQueue = (slipId) => localStorage.removeItem(key(slipId));
    const receiptKey = (slipId) => `${receiptStoragePrefix}${slipId}`;
    const readReceipt = (slipId) => {
        try {
            return JSON.parse(sessionStorage.getItem(receiptKey(slipId)) || 'null');
        } catch {
            sessionStorage.removeItem(receiptKey(slipId));
            return null;
        }
    };
    const writeReceipt = (slipId, value) => {
        try {
            sessionStorage.setItem(receiptKey(slipId), JSON.stringify(value));
        } catch {
            // Reprint falls back to its read endpoint when session storage is unavailable.
        }
    };
    const post = async (url, payload) => {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                Accept: 'application/json',
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest',
                ...(token() ? { RequestVerificationToken: token() } : {})
            },
            body: JSON.stringify(payload)
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.succeeded) throw new Error(data?.message || '会計処理に失敗しました。');
        return data;
    };
    const mutationCommand = (action, slipId, payload = {}) => {
        const commandKey = `${action}:${slipId}`;
        const existing = pendingMutationCommands.get(commandKey);
        if (existing) return { commandKey, command: existing };
        const state = window.ProsperBusinessHomeState || {};
        if (!state.businessDayId || state.businessDayRevision == null) {
            throw new Error('営業中データの同期後に会計してください。');
        }
        const command = {
            operationId: crypto.randomUUID(),
            expectedBusinessDayId: Number(state.businessDayId),
            expectedBusinessDayRevision: Number(state.businessDayRevision),
            slipId: Number(slipId),
            ...payload
        };
        pendingMutationCommands.set(commandKey, command);
        persistPendingMutations();
        syncPaymentMutationLock();
        return { commandKey, command };
    };
    const postMutation = async (url, action, slipId, payload = {}) => {
        const { commandKey, command } = mutationCommand(action, slipId, payload);
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest',
                    ...(token() ? { RequestVerificationToken: token() } : {})
                },
                body: JSON.stringify(command)
            });
            const data = await response.json().catch(() => null);
            const classification = saveResponse?.classify({ response, payload: data }) ?? {
                confirmed: data?.status === 'confirmed',
                terminal: ['confirmed', 'conflict', 'validation_error', 'permission_denied', 'stale_work_item'].includes(data?.status),
                retry: !data || response.status >= 500 || data?.status === 'unavailable'
            };
            if (data?.businessSnapshot) {
                document.dispatchEvent(new CustomEvent('prosper:business-home-mutation-confirmed', {
                    detail: { snapshot: data.businessSnapshot }
                }));
            }
            if (classification.terminal) {
                deletePendingMutation(commandKey);
            }
            if (!classification.confirmed) {
                throw new Error(data?.message || '会計処理に失敗しました。');
            }
            deletePendingMutation(commandKey);
            return data;
        } catch (error) {
            throw error;
        }
    };
    const runExclusive = async (action, showOverlay = true) => {
        if (isActionInFlight) {
            return;
        }

        isActionInFlight = true;
        if (showOverlay) window.AppLoading?.show();
        try {
            return await action();
        } finally {
            isActionInFlight = false;
            if (showOverlay) window.AppLoading?.hide();
        }
    };
    const composeClosedAt = () => {
        const time = closedTime.value;
        const [hour, minute] = time.split(':').map(Number);
        if (!Number.isFinite(hour) || !Number.isFinite(minute) || !config.businessDate) return null;
        const date = new Date(`${config.businessDate}T00:00:00+09:00`);
        if (hour < 12) date.setDate(date.getDate() + 1);
        date.setHours(hour, minute, 0, 0);
        return date.toISOString();
    };
    const setDefaultClosedTime = () => {
        const now = new Date();
        const rounded = new Date(now);
        const remainder = rounded.getMinutes() % 5;
        if (remainder !== 0 || rounded.getSeconds() !== 0 || rounded.getMilliseconds() !== 0) {
            rounded.setMinutes(rounded.getMinutes() + (5 - remainder), 0, 0);
        }

        const businessHour = rounded.getHours() < 12 ? rounded.getHours() + 24 : rounded.getHours();
        const value = `${String(businessHour).padStart(2, '0')}:${String(rounded.getMinutes()).padStart(2, '0')}`;
        if (Array.from(closedTime.options).some((option) => option.value === value)) {
            closedTime.value = value;
        }
    };
    const text = (element, value) => { element.textContent = String(value); };
    const showStatement = (printData, reviewData = {}) => {
        const summary = root.querySelector('[data-business-statement-summary]');
        const orders = root.querySelector('[data-business-statement-orders]');
        const adjustments = root.querySelector('[data-business-statement-adjustments]');
        const totals = root.querySelector('[data-business-statement-totals]');
        summary.replaceChildren();
        [
            ['卓番', printData.table_display_name], ['入店', formatTime(printData.opened_at)],
            ['退店', formatTime(printData.closed_at)], ['お客様数', `${printData.customer_count}人`], ['合計', yen(printData.total_amount)]
        ].forEach(([label, value]) => {
            const item = document.createElement('div');
            const labelElement = document.createElement('span'); labelElement.textContent = label;
            const valueElement = document.createElement('strong'); valueElement.textContent = value || '-';
            item.append(labelElement, valueElement); summary.appendChild(item);
        });
        const groupOrders = (lines) => {
            const groups = new Map();
            (Array.isArray(lines) ? lines : []).forEach((line) => {
                const name = line?.name || '-';
                const backCastName = String(line?.back_cast_display_name || '').trim();
                const unitPrice = Math.round(Number(line?.unit_price) || 0);
                const key = `${name}\u0000${backCastName}\u0000${unitPrice}`;
                const current = groups.get(key) || {
                    name: backCastName ? `${name}/${backCastName}` : name,
                    unit_price: unitPrice,
                    quantity: 0,
                    amount: 0
                };
                current.quantity += Number(line?.quantity) || 0;
                current.amount += Number(line?.amount) || 0;
                groups.set(key, current);
            });
            return Array.from(groups.values());
        };
        const lineRows = (lines, target, noData) => {
            if (!target) return;
            target.replaceChildren();
            if (!Array.isArray(lines) || lines.length === 0) { target.textContent = noData; return; }
            lines.forEach((line) => {
                const row = document.createElement('div'); row.className = 'checkout-review__order';
                row.append(Object.assign(document.createElement('strong'), { textContent: line.name || '-' }));
                row.append(Object.assign(document.createElement('span'), { textContent: line.quantity == null ? '' : `${yen(line.unit_price)} × ${line.quantity}` }));
                row.append(Object.assign(document.createElement('strong'), { textContent: yen(line.amount) }));
                target.appendChild(row);
            });
        };
        lineRows(groupOrders(printData.orders), orders, '注文はありません。');
        lineRows(printData.adjustments, adjustments, '調整はありません。');
        totals.replaceChildren();
        [
            ['小計', printData.subtotal_amount], ['サービス料', printData.service_charge_amount],
            ['合計', printData.total_amount], ['（内消費税額）', printData.consumption_tax_amount]
        ].forEach(([label, amount]) => {
            const row = document.createElement('div');
            if (label === '合計') row.classList.add('checkout-review__total');
            if (label === '（内消費税額）') row.classList.add('checkout-review__tax');
            row.append(Object.assign(document.createElement('span'), { textContent: label }));
            row.append(Object.assign(document.createElement('strong'), { textContent: yen(amount) }));
            totals.appendChild(row);
        });
        statement.hidden = false;
    };
    const formatTime = (value) => value ? new Intl.DateTimeFormat('ja-JP', { hour: '2-digit', minute: '2-digit' }).format(new Date(value)) : '-';
    const syncFooterActions = () => {
        const isCheckoutReady = current?.status === 'checkout_ready';
        const hasCheckoutQueue = isCheckoutReady && Boolean(current?.queue);
        const printing = Boolean(current?.statementPrintTask);
        issueAction.hidden = isCheckoutReady;
        printActions.hidden = !hasCheckoutQueue;
        printStatementButton.disabled = !hasCheckoutQueue || printing;
        releaseCheckoutButton.disabled = !hasCheckoutQueue || printing;
        proceedPaymentButton.disabled = !hasCheckoutQueue;
    };
    const renderPrintState = () => {
        const state = current?.queue?.state || 'pending';
        const label = {
            pending: '会計伝票を印刷します。必要なら再印刷できます。',
            printing: '会計伝票を印刷しています。',
            failed: '会計伝票を印刷できませんでした。必要なら再印刷してください。',
            printed: '会計伝票を印刷しました。必要なら再印刷できます。'
        }[state] || '会計伝票を印刷します。必要なら再印刷できます。';
        text(printState, label);
        printPanel.hidden = false;
        syncFooterActions();
    };
    const renderPayments = () => {
        const total = Math.round(Number(current?.queue?.printData?.total_amount) || 0);
        paymentRows.replaceChildren();
        if (total === 0) {
            paymentRows.textContent = '請求なし 0円';
            renderPaymentSummary();
            return;
        }
        paymentMethods.forEach((method) => {
            const code = String(method.methodCode);
            const name = String(method.methodName);
            const row = document.createElement('div'); row.className = 'checkout-payment-row';
            const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-primary checkout-payment-row__button';
            button.dataset.paymentCode = code; button.textContent = name;
            const input = document.createElement('input'); input.type = 'number'; input.min = '0'; input.step = '1'; input.className = 'form-control checkout-payment-row__amount';
            input.dataset.paymentAmount = code; input.placeholder = '金額'; input.disabled = true;
            row.append(button, input); paymentRows.appendChild(row);
        });
        renderPaymentSummary();
    };
    const renderPaymentDetail = () => {
        const slip = businessHome()?.getSlip?.(current?.slipId);
        const buildDetail = businessHome()?.buildSlipDetailContent;
        if (!paymentDetail || !paymentForm || !slip || typeof buildDetail !== 'function') return;
        const detail = buildDetail(
            {
                ...slip,
                status: current?.status || slip.status,
                statusDisplay: current?.status === 'checkout_ready' ? '会計準備中' : slip.statusDisplay,
                checkoutPending: false
            },
            { includeEditorActions: false }
        );
        const activity = detail.querySelector('.business-slip-detail-layout__activity');
        if (!activity) return;
        activity.appendChild(paymentForm);
        paymentDetail.replaceChildren(detail);
    };
    const selectedPayments = () => Array.from(paymentRows.querySelectorAll('[data-payment-code]')).map((button) => {
        const code = button.dataset.paymentCode;
        const input = paymentRows.querySelector(`[data-payment-amount="${code}"]`);
        return {
            methodCode: code,
            isSelected: button.classList.contains('btn-primary'),
            amount: Math.round(Number(input?.value) || 0),
            requiresReceivedAmount: paymentMethodByCode.get(code)?.requiresReceivedAmount === true
        };
    }).filter((payment) => payment.isSelected);
    const paymentValidation = () => {
        const total = Math.round(Number(current?.queue?.printData?.total_amount) || 0);
        const payments = selectedPayments();
        const paid = payments.reduce((sum, payment) => sum + payment.amount, 0);
        const receivedPayment = payments.find((payment) => payment.requiresReceivedAmount);
        const receivedEntered = receivedAmount.value !== '';
        const received = Math.round(Number(receivedAmount.value) || 0);

        if (total === 0) {
            return {
                total,
                payments,
                paid,
                receivedPayment,
                received,
                canConfirm: payments.length === 0,
                message: payments.length === 0 ? '' : '0円会計では決済方法を選択しません。'
            };
        }

        if (payments.length === 0) {
            return { total, payments, paid, receivedPayment, received, canConfirm: false, message: '決済方法を選択してください。' };
        }

        if (payments.some((payment) => payment.amount <= 0)) {
            return { total, payments, paid, receivedPayment, received, canConfirm: false, message: '決済金額は1円以上で入力してください。' };
        }

        if (paid !== total) {
            return { total, payments, paid, receivedPayment, received, canConfirm: false, message: '決済合計を会計額に合わせてください。' };
        }

        if (payments.filter((payment) => payment.requiresReceivedAmount).length > 1) {
            return { total, payments, paid, receivedPayment, received, canConfirm: false, message: '受取額を入力する決済方法は1つだけ選択してください。' };
        }

        if (receivedPayment && (!receivedEntered || received < receivedPayment.amount)) {
            return { total, payments, paid, receivedPayment, received, canConfirm: false, message: '受取額を確認してください。' };
        }

        return { total, payments, paid, receivedPayment, received, canConfirm: true, message: '' };
    };
    const renderPaymentSummary = () => {
        const validation = paymentValidation();
        const { total, payments, paid, receivedPayment, received } = validation;
        receivedRow.hidden = !receivedPayment;
        if (!receivedPayment) receivedAmount.value = '';
        paymentSummary.replaceChildren();
        [['会計額', yen(total)], ['決済合計', yen(paid)], ['差額', yen(total - paid)]].forEach(([label, value]) => {
            const item = document.createElement('div'); item.append(Object.assign(document.createElement('span'), { textContent: label }), Object.assign(document.createElement('strong'), { textContent: value })); paymentSummary.appendChild(item);
        });
        if (receivedPayment && received >= receivedPayment.amount) {
            const item = document.createElement('div'); item.append(Object.assign(document.createElement('span'), { textContent: '釣銭' }), Object.assign(document.createElement('strong'), { textContent: yen(received - receivedPayment.amount) })); paymentSummary.appendChild(item);
        }
        if (!validation.canConfirm && validation.message) {
            const item = document.createElement('div');
            item.className = 'checkout-payment-summary__warning';
            item.append(Object.assign(document.createElement('span'), { textContent: '確認' }), Object.assign(document.createElement('strong'), { textContent: validation.message }));
            paymentSummary.appendChild(item);
        }
        if (confirmButton) {
            confirmButton.disabled = !validation.canConfirm || isActionInFlight;
        }
        syncPaymentMutationLock();
    };
    const showCurrent = () => {
        if (!current?.queue) return;
        table.textContent = current.tableDisplay || '会計';
        issuePanel.hidden = true;
        showStatement(current.queue.printData, current.queue.reviewData);
        renderPrintState();
        renderPayments();
    };
    const reset = () => {
        if (current?.slipId) {
            businessHome()?.setCheckoutLock?.(current.slipId, false);
        }
        current = null; setMessage(); setPaymentMessage(); statement.hidden = true; printPanel.hidden = true; issuePanel.hidden = false;
        receivedAmount.value = ''; addressee.value = ''; paymentRows.replaceChildren(); paymentSummary.replaceChildren();
        if (paymentDetail && paymentForm) paymentDetail.replaceChildren(paymentForm);
        syncFooterActions();
    };
    const open = async (slip) => {
        if (isActionInFlight) return;
        const requestedSlipId = Number(slip.id);
        businessHome()?.setCheckoutLock?.(requestedSlipId, true);
        await runExclusive(async () => {
            const synchronized = await businessHome()?.waitForOperations?.();
            if (synchronized === false) {
                businessHome()?.setCheckoutLock?.(requestedSlipId, false);
                await showDialogAlert('保存結果を確認できない変更があります。営業中一覧の同期後に会計してください。');
                return;
            }
            const latestSlip = businessHome()?.getSlip?.(requestedSlipId);
            if (!latestSlip || !['open', 'checkout_ready'].includes(latestSlip.status)) {
                businessHome()?.setCheckoutLock?.(requestedSlipId, false);
                await showDialogAlert('対象伝票の状態が変わりました。営業中一覧を確認してください。');
                return;
            }

            reset(); current = { slipId: Number(latestSlip.id), tableDisplay: latestSlip.tableDisplay, status: latestSlip.status, step: 'statement', queue: readQueue(latestSlip.id) };
            table.textContent = latestSlip.tableDisplay || '会計';
            modal?.show();
            if (latestSlip.status === 'open') setDefaultClosedTime();
            syncFooterActions();
            if (latestSlip.status !== 'checkout_ready') return;
            if (current.queue?.printData) {
                showCurrent();
                return;
            }
            try {
                const data = await post(config.getCheckoutStatementPrintDataUrl, { slipId: current.slipId });
                current.queue ??= { state: 'pending', printData: data.printData, reviewData: data.reviewData };
                current.queue.printData = data.printData;
                current.queue.reviewData = data.reviewData;
                writeQueue(); showCurrent();
            } catch (error) { setMessage(error.message); }
        });
    };
    const issue = async () => {
        const closedAt = composeClosedAt();
        if (!current || !closedAt) { setMessage('退店時刻を確認してください。'); return; }
        await runExclusive(async () => {
            try {
                const synchronized = await businessHome()?.waitForOperations?.();
                if (synchronized === false) {
                    setMessage('保存結果を確認できない変更があります。同期後に会計伝票を出力してください。');
                    return;
                }
                const saved = await businessHome()?.flush?.();
                if (saved === false) {
                    setMessage('未保存のカラオケ回数を保存できませんでした。');
                    return;
                }
                businessHome()?.setCheckoutLock?.(current.slipId, true);
                setMessage('会計伝票を発行中です。');
                const data = await postMutation(
                    config.issueCheckoutStatementUrl,
                    'issue',
                    current.slipId,
                    { closedAt });
                current.status = 'checkout_ready';
                current.step = 'statement';
                current.queue = {
                    state: 'pending',
                    printData: data.statementPrintData,
                    reviewData: data.statementReviewData
                }; writeQueue(); showCurrent();
                void printStatementCore();
                businessHome()?.setCheckoutLock?.(current.slipId, false);
            } catch (error) {
                businessHome()?.setCheckoutLock?.(current?.slipId, false);
                setMessage(error.message);
            }
        }, false);
    };
    const printStatementCore = () => {
        const checkout = current;
        if (!checkout?.queue) return Promise.resolve();
        if (checkout.statementPrintTask) return checkout.statementPrintTask;

        checkout.queue.state = 'printing';
        writeQueueFor(checkout);
        const task = Promise.resolve().then(async () => {
            try {
                if (!config.receiptPrinterEnabled || !window.ProsperCheckoutStatementPrinter?.print) throw new Error('会計伝票プリンターを利用できません。');
                await window.ProsperCheckoutStatementPrinter.print(checkout.queue.printData);
                checkout.queue.state = 'printed';
            } catch (error) {
                checkout.queue.state = 'failed';
                if (current === checkout) setMessage(`会計伝票を印刷できませんでした。${error.message}`);
            } finally {
                checkout.statementPrintTask = null;
                writeQueueFor(checkout);
                if (current === checkout) renderPrintState();
            }
        });
        checkout.statementPrintTask = task;
        if (current === checkout) renderPrintState();
        return task;
    };
    const printStatement = () => {
        if (!current?.queue || current.statementPrintTask) return;
        void printStatementCore();
    };
    const release = async (slip = null) => {
        const slipId = Number(slip?.id || current?.slipId || 0);
        const tableDisplay = slip?.tableDisplay || current?.tableDisplay || 'この伝票';
        if (isActionInFlight || !slipId || (!slip && current?.statementPrintTask)
            || !(await showDialogConfirm(`${tableDisplay} の会計準備を解除して編集可能に戻しますか？`))) return;
        await runExclusive(async () => {
            try {
                await postMutation(config.releaseCheckoutReadyUrl, 'release', slipId);
                removeQueue(slipId);
                if (current && Number(current.slipId) === slipId) modal?.hide();
            } catch (error) {
                if (slip) {
                    await showDialogAlert(error.message);
                } else {
                    setMessage(error.message);
                }
            }
        });
    };
    const proceedPayment = () => {
        if (!current?.queue || current.status !== 'checkout_ready') return;
        current.step = 'payment';
        paymentTable.textContent = current.tableDisplay || '会計';
        setPaymentMessage();
        renderPayments();
        renderPaymentDetail();
        modalTransition = 'to-payment';
        modal?.hide();
    };
    const confirm = async () => {
        if (!current || current.step !== 'payment') return;
        const validation = paymentValidation();
        if (!validation.canConfirm) {
            setPaymentMessage(validation.message || '決済内容を確認してください。');
            renderPaymentSummary();
            return;
        }

        const payments = validation.payments;
        await runExclusive(async () => {
            try {
                const data = await postMutation(
                    config.confirmCheckoutUrl,
                    'confirm',
                    current.slipId,
                    { payments, receivedAmount: receivedAmount.value === '' ? null : validation.received });
                current.queueDiscarded = true;
                removeQueue(current.slipId);
                paymentModalCloseIsProgrammatic = true;
                paymentModal?.hide();
                modal?.hide();
                const receipt = { ...data.receiptPrintData, checkoutId: data.checkoutId, addressee: addressee.value || '' };
                writeReceipt(current.slipId, receipt);
                const printReceipt = () => window.ProsperSiiReceiptPrinterApi?.print(receipt)?.catch(() => {});
                printReceipt();
            } catch (error) { setPaymentMessage(error.message); }
        });
    };
    const printReceipt = async (slip) => {
        if (isActionInFlight) return;
        await runExclusive(async () => {
            try {
                const cached = readReceipt(Number(slip.id));
                if (cached) {
                    await window.ProsperSiiReceiptPrinterApi?.print(cached);
                    return;
                }
                const data = await post(config.getCheckoutReceiptPrintDataUrl, { slipId: Number(slip.id) });
                const receipt = { ...data.printData, checkoutId: data.checkoutId, addressee: '' };
                writeReceipt(Number(slip.id), receipt);
                await window.ProsperSiiReceiptPrinterApi?.print(receipt);
            } catch (error) { await showDialogAlert(error.message); }
        });
    };
    const cancelCheckout = async (slip) => {
        if (isActionInFlight || !(await showDialogConfirm(`${slip.tableDisplay || 'この伝票'} の会計を取消しますか？`))) return;
        await runExclusive(async () => {
            try {
                const data = await postMutation(config.cancelCheckoutUrl, 'cancel', Number(slip.id));
                sessionStorage.removeItem(receiptKey(Number(slip.id)));
                window.ProsperSiiReceiptPrinterApi?.clearReceiptTerminalState(data.checkoutId);
            } catch (error) { await showDialogAlert(error.message); }
        });
    };

    document.addEventListener('click', (event) => {
        const checkout = event.target.closest('[data-business-start-checkout]');
        const releaseReady = event.target.closest('[data-business-release-checkout-ready]');
        const receipt = event.target.closest('[data-business-print-receipt]');
        const cancel = event.target.closest('[data-business-cancel-checkout]');
        if (checkout) { const slip = businessHome()?.getSlip?.(checkout.dataset.businessStartCheckout); if (slip) void open(slip); }
        if (releaseReady) { const slip = businessHome()?.getSlip?.(releaseReady.dataset.businessReleaseCheckoutReady); if (slip) void release(slip); }
        if (receipt) { const slip = businessHome()?.getSlip?.(receipt.dataset.businessPrintReceipt); if (slip) void printReceipt(slip); }
        if (cancel) { const slip = businessHome()?.getSlip?.(cancel.dataset.businessCancelCheckout); if (slip) void cancelCheckout(slip); }
    });
    root.querySelector('[data-business-issue-statement]')?.addEventListener('click', () => void issue());
    root.querySelector('[data-business-print-statement]')?.addEventListener('click', () => void printStatement());
    root.querySelector('[data-business-release-checkout]')?.addEventListener('click', () => void release());
    root.querySelector('[data-business-proceed-payment]')?.addEventListener('click', proceedPayment);
    confirmButton?.addEventListener('click', () => void confirm());
    paymentRows?.addEventListener('click', (event) => {
        if (isActionInFlight) return;
        const button = event.target.closest('[data-payment-code]'); if (!button) return;
        const selected = !button.classList.contains('btn-primary'); const input = paymentRows.querySelector(`[data-payment-amount="${button.dataset.paymentCode}"]`);
        const row = button.closest('.checkout-payment-row');
        button.classList.toggle('btn-primary', selected); button.classList.toggle('btn-outline-primary', !selected); input.disabled = !selected;
        row?.classList.toggle('checkout-payment-row--selected', selected);
        if (selected && !input.value) {
            const currentPaid = selectedPayments().filter((payment) => payment.methodCode !== button.dataset.paymentCode).reduce((sum, payment) => sum + payment.amount, 0);
            input.value = Math.max(0, Math.round(Number(current?.queue?.printData?.total_amount) || 0) - currentPaid);
        }
        if (!selected) {
            input.value = '';
            if (paymentMethodByCode.get(button.dataset.paymentCode)?.requiresReceivedAmount === true) {
                receivedAmount.value = '';
            }
        }
        renderPaymentSummary();
    });
    paymentRows?.addEventListener('input', () => { if (!isActionInFlight) renderPaymentSummary(); });
    receivedAmount?.addEventListener('input', () => { if (!isActionInFlight) renderPaymentSummary(); });
    modalElement?.addEventListener('hidden.bs.modal', () => {
        if (modalTransition === 'to-payment') {
            modalTransition = null;
            paymentModal?.show();
            return;
        }
        reset();
    });
    paymentModalElement?.addEventListener('hide.bs.modal', (event) => {
        if (isActionInFlight && !paymentModalCloseIsProgrammatic) event.preventDefault();
    });
    paymentModalElement?.addEventListener('hidden.bs.modal', () => {
        const wasProgrammatic = paymentModalCloseIsProgrammatic;
        paymentModalCloseIsProgrammatic = false;
        if (wasProgrammatic) {
            reset();
            return;
        }
        if (!current || current.step !== 'payment') return;
        current.step = 'statement';
        receivedAmount.value = ''; addressee.value = ''; paymentRows.replaceChildren(); paymentSummary.replaceChildren();
        setPaymentMessage();
        showCurrent();
        modal?.show();
    });
    window.ProsperBusinessCheckout = { open };
})();
