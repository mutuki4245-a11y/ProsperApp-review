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
    let current = null;
    let isActionInFlight = false;
    let paymentModalCloseIsProgrammatic = false;
    let modalTransition = null;

    const businessHome = () => window.ProsperBusinessHome;
    const yen = (value) => `${Math.round(Number(value) || 0).toLocaleString('ja-JP')}円`;
    const token = () => sourceForm.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
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
            ['退店', formatTime(printData.closed_at)], ['客数', `${printData.customer_count}人`], ['合計', yen(printData.total_amount)]
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
        [ ['cash', '現金'], ['cat', 'クレジット'], ['paypay', 'PAYPAY'] ].forEach(([code, name]) => {
            const row = document.createElement('div'); row.className = 'checkout-payment-row';
            const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-primary checkout-payment-row__button';
            button.dataset.paymentCode = code; button.textContent = name;
            const input = document.createElement('input'); input.type = 'number'; input.min = '0'; input.step = '1'; input.className = 'form-control checkout-payment-row__amount';
            input.dataset.paymentAmount = code; input.placeholder = '金額'; input.disabled = true;
            row.append(button, input); paymentRows.appendChild(row);
        });
        renderPaymentSummary();
    };
    const selectedPayments = () => Array.from(paymentRows.querySelectorAll('[data-payment-code]')).map((button) => {
        const code = button.dataset.paymentCode;
        const input = paymentRows.querySelector(`[data-payment-amount="${code}"]`);
        return { methodCode: code, isSelected: button.classList.contains('btn-primary'), amount: Math.round(Number(input?.value) || 0) };
    }).filter((payment) => payment.isSelected);
    const renderPaymentSummary = () => {
        const total = Math.round(Number(current?.queue?.printData?.total_amount) || 0);
        const payments = selectedPayments();
        const paid = payments.reduce((sum, payment) => sum + payment.amount, 0);
        const cash = payments.find((payment) => payment.methodCode === 'cash');
        receivedRow.hidden = !cash;
        if (!cash) receivedAmount.value = '';
        const received = Math.round(Number(receivedAmount.value) || 0);
        paymentSummary.replaceChildren();
        [['会計額', yen(total)], ['決済合計', yen(paid)], ['差額', yen(total - paid)]].forEach(([label, value]) => {
            const item = document.createElement('div'); item.append(Object.assign(document.createElement('span'), { textContent: label }), Object.assign(document.createElement('strong'), { textContent: value })); paymentSummary.appendChild(item);
        });
        if (cash && received >= cash.amount) {
            const item = document.createElement('div'); item.append(Object.assign(document.createElement('span'), { textContent: '釣銭' }), Object.assign(document.createElement('strong'), { textContent: yen(received - cash.amount) })); paymentSummary.appendChild(item);
        }
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
                window.alert('保存結果を確認できない変更があります。営業中一覧の同期後に会計してください。');
                return;
            }
            const latestSlip = businessHome()?.getSlip?.(requestedSlipId);
            if (!latestSlip || !['open', 'checkout_ready'].includes(latestSlip.status)) {
                businessHome()?.setCheckoutLock?.(requestedSlipId, false);
                window.alert('対象伝票の状態が変わりました。営業中一覧を確認してください。');
                return;
            }

            reset(); current = { slipId: Number(latestSlip.id), tableDisplay: latestSlip.tableDisplay, status: latestSlip.status, step: 'statement', queue: readQueue(latestSlip.id) };
            table.textContent = latestSlip.tableDisplay || '会計';
            modal?.show();
            if (latestSlip.status === 'open') setDefaultClosedTime();
            syncFooterActions();
            if (latestSlip.status !== 'checkout_ready') return;
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
                const data = await post(config.issueCheckoutStatementUrl, { slipId: current.slipId, closedAt });
                current.status = 'checkout_ready';
                current.step = 'statement';
                current.queue = { state: 'pending', printData: data.printData, reviewData: data.reviewData }; writeQueue(); showCurrent();
                void printStatementCore();
                const snapshotSynchronized = await businessHome()?.reload?.();
                if (snapshotSynchronized) {
                    businessHome()?.setCheckoutLock?.(current.slipId, false);
                } else {
                    setMessage('会計準備中です。最新状態を取得できるまで編集はできません。');
                }
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
    const release = async () => {
        if (isActionInFlight || !current || current.statementPrintTask || !window.confirm('会計準備を解除して伝票を編集可能に戻しますか？')) return;
        await runExclusive(async () => {
            try {
                await post(config.releaseCheckoutReadyUrl, { slipId: current.slipId });
                removeQueue(current.slipId); modal?.hide(); window.dispatchEvent(new CustomEvent('prosper:business-slips-refresh'));
            } catch (error) { setMessage(error.message); }
        });
    };
    const proceedPayment = () => {
        if (!current?.queue || current.status !== 'checkout_ready') return;
        current.step = 'payment';
        paymentTable.textContent = current.tableDisplay || '会計';
        setPaymentMessage();
        renderPayments();
        confirmButton.disabled = false;
        modalTransition = 'to-payment';
        modal?.hide();
    };
    const confirm = async () => {
        if (!current || current.step !== 'payment') return;
        const payments = selectedPayments();
        const total = Math.round(Number(current.queue.printData.total_amount) || 0);
        if ((total === 0 && payments.length > 0) || (total > 0 && payments.length === 0)) { setPaymentMessage('決済方法を確認してください。'); return; }
        await runExclusive(async () => {
            try {
                const data = await post(config.confirmCheckoutUrl, { slipId: current.slipId, payments, receivedAmount: receivedAmount.value === '' ? null : Math.round(Number(receivedAmount.value) || 0) });
                current.queueDiscarded = true;
                removeQueue(current.slipId);
                paymentModalCloseIsProgrammatic = true;
                paymentModal?.hide();
                modal?.hide();
                window.dispatchEvent(new CustomEvent('prosper:business-slips-refresh'));
                const receipt = { ...data.printData, checkoutId: data.checkoutId, addressee: addressee.value || '' };
                const printReceipt = () => window.ProsperSiiReceiptPrinterApi?.print(receipt)?.catch(() => {});
                printReceipt();
            } catch (error) { setPaymentMessage(error.message); }
        });
    };
    const printReceipt = async (slip) => {
        if (isActionInFlight) return;
        await runExclusive(async () => {
            try {
                const data = await post(config.getCheckoutReceiptPrintDataUrl, { slipId: Number(slip.id) });
                await window.ProsperSiiReceiptPrinterApi?.print({ ...data.printData, checkoutId: data.checkoutId, addressee: '' });
            } catch (error) { window.alert(error.message); }
        });
    };
    const cancelCheckout = async (slip) => {
        if (isActionInFlight || !window.confirm(`${slip.tableDisplay || 'この伝票'} の会計を取消しますか？`)) return;
        await runExclusive(async () => {
            try {
                const data = await post(config.cancelCheckoutUrl, { slipId: Number(slip.id) });
                window.ProsperSiiReceiptPrinterApi?.clearReceiptTerminalState(data.checkoutId);
                window.dispatchEvent(new CustomEvent('prosper:business-slips-refresh'));
            } catch (error) { window.alert(error.message); }
        });
    };

    document.addEventListener('click', (event) => {
        const checkout = event.target.closest('[data-business-start-checkout]');
        const receipt = event.target.closest('[data-business-print-receipt]');
        const cancel = event.target.closest('[data-business-cancel-checkout]');
        if (checkout) { const slip = businessHome()?.getSlip?.(checkout.dataset.businessStartCheckout); if (slip) void open(slip); }
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
            if (button.dataset.paymentCode === 'cash') receivedAmount.value = '';
        }
        renderPaymentSummary();
    });
    paymentRows?.addEventListener('input', () => { if (!isActionInFlight) renderPaymentSummary(); });
    receivedAmount?.addEventListener('input', () => { if (!isActionInFlight) renderPaymentSummary(); });
    window.addEventListener('prosper:business-slips-refresh', () => businessHome()?.reload?.());
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
