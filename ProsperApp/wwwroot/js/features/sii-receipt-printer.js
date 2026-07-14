(() => {
    const requestElement = document.getElementById('pendingCheckoutReceiptPrintRequest');
    const statusElement = document.querySelector('[data-receipt-print-status]');
    const reprintPanel = document.querySelector('[data-receipt-reprint-panel]');
    const reprintList = document.querySelector('[data-receipt-reprint-list]');
    const config = window.prosperSiiReceiptPrinter ?? {};
    const pendingStorageKey = 'prosper:receipt-reprints:v1';

    const setStatus = (message, state) => {
        if (!statusElement) {
            return;
        }

        statusElement.hidden = false;
        statusElement.textContent = message;
        statusElement.classList.remove('alert-info', 'alert-success', 'alert-warning');
        statusElement.classList.add(state === 'success' ? 'alert-success' : state === 'warning' ? 'alert-warning' : 'alert-info');
    };

    const toAmount = (value) => Math.round(Number(value) || 0);
    const formatYen = (value) => `${toAmount(value).toLocaleString('ja-JP')}円`;
    const compact = (value, fallback = '') => String(value ?? fallback).trim();

    const receiptWidth = Number.isFinite(Number(config.lineWidth)) && Number(config.lineWidth) >= 24
        ? Number(config.lineWidth)
        : 48;
    const revenueStampThreshold = 50001;
    const consumptionTaxRate = 0.10;

    const formatDateTime = (value) => new Intl.DateTimeFormat('ja-JP', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
    }).format(value);

    const charWidth = (char) => {
        const code = char.codePointAt(0) ?? 0;
        return code > 0x00ff ? 2 : 1;
    };

    const textWidth = (text) => Array.from(String(text)).reduce((total, char) => total + charWidth(char), 0);
    const spaces = (count) => ' '.repeat(Math.max(0, count));
    const separator = () => '-'.repeat(receiptWidth);

    const centerLine = (text) => {
        const value = String(text);
        const left = Math.floor((receiptWidth - textWidth(value)) / 2);
        return `${spaces(left)}${value}`;
    };

    const twoColumnLine = (left, right) => {
        const leftValue = String(left);
        const rightValue = String(right);
        return `${leftValue}${spaces(receiptWidth - textWidth(leftValue) - textWidth(rightValue))}${rightValue}`;
    };

    const paymentText = (request) => {
        const payments = Array.isArray(request.payments) ? request.payments : [];
        if (payments.length === 0) {
            return '-';
        }

        return payments
            .map((payment) => compact(payment.methodName, payment.methodCode))
            .filter((methodName) => methodName.length > 0)
            .join(' / ') || '-';
    };

    const appendPaymentLine = (lines, value) => {
        const label = '支払い方法';
        if (textWidth(value) <= receiptWidth - textWidth(label)) {
            lines.push(twoColumnLine(label, value));
            return;
        }

        lines.push(`${label}:`);
        value.split(' / ').forEach((methodName) => {
            lines.push(`  ${methodName}`);
        });
    };

    const consumptionTaxAmount = (amount) =>
        Math.round(toAmount(amount) * consumptionTaxRate / (1 + consumptionTaxRate));

    const appendRevenueStampBox = (lines) => {
        lines.push('');
        lines.push('収入印紙欄');
        lines.push('+------------------------------+');
        lines.push('|                              |');
        lines.push('|                              |');
        lines.push('+------------------------------+');
    };

    const buildReceiptText = (request) => {
        const lines = [];
        const totalAmount = toAmount(request.totalAmount);

        lines.push(compact(request.storeName, '店舗'));
        lines.push(centerLine('領収書'));
        lines.push(separator());
        lines.push(twoColumnLine('現在時刻', formatDateTime(new Date())));
        lines.push(twoColumnLine('伝票番号', compact(request.slipNo, request.slipId)));
        lines.push('');
        lines.push(centerLine('飲食代として'));
        lines.push('');
        lines.push(twoColumnLine('会計額', formatYen(totalAmount)));
        appendPaymentLine(lines, paymentText(request));
        lines.push(twoColumnLine('内消費税額', formatYen(consumptionTaxAmount(totalAmount))));

        if (totalAmount >= revenueStampThreshold) {
            appendRevenueStampBox(lines);
        }

        lines.push(separator());
        lines.push('');
        lines.push('');
        return `${lines.join('\n')}\n`;
    };

    const parseRequestElement = () => {
        try {
            return JSON.parse(requestElement?.textContent || '{}');
        } catch (error) {
            const parseError = new Error('領収書印刷データを読み込めませんでした。');
            parseError.cause = error;
            throw parseError;
        }
    };

    const assertSdkResult = (label, result) => {
        if (!result || Number(result.errorCode) !== 0) {
            const detail = result
                ? `errorCode=${result.errorCode}, errorString=${result.errorString ?? ''}, errorExtendedString=${result.errorExtendedString ?? ''}`
                : 'no result';
            throw new Error(`${label} failed: ${detail}`);
        }
    };

    const trySdkCall = async (label, call, fatal = true) => {
        try {
            const result = await call();
            assertSdkResult(label, result);
            return result;
        } catch (error) {
            if (fatal) {
                throw error;
            }

            console.warn(`SII receipt printer optional step failed: ${label}`, error);
            return null;
        }
    };

    const receiptKey = (request) => compact(request.checkoutId, `${request.slipId}:${request.closedAt}`);

    const readPendingReceipts = () => {
        try {
            const stored = JSON.parse(localStorage.getItem(pendingStorageKey) || '[]');
            return Array.isArray(stored) ? stored.filter((item) => item?.key && item?.request) : [];
        } catch {
            return [];
        }
    };

    const writePendingReceipts = (items) => {
        try {
            localStorage.setItem(pendingStorageKey, JSON.stringify(items));
        } catch {
        }
    };

    const removePendingReceipt = (key) => {
        writePendingReceipts(readPendingReceipts().filter((item) => item.key !== key));
        renderPendingReceipts();
    };

    const upsertPendingReceipt = (request, message) => {
        const key = receiptKey(request);
        const items = readPendingReceipts().filter((item) => item.key !== key);
        items.unshift({
            key,
            request,
            message: compact(message, '印刷できませんでした。'),
            failedAt: new Date().toISOString()
        });
        writePendingReceipts(items.slice(0, 20));
        renderPendingReceipts();
    };

    const describeReceipt = (request) => {
        const table = compact(request.tableDisplayName, '-');
        const slipNo = compact(request.slipNo, request.slipId);
        return `${table} / 伝票 ${slipNo} / ${formatYen(request.totalAmount)}`;
    };

    const renderPendingReceipts = () => {
        if (!reprintPanel || !reprintList) {
            return;
        }

        const items = readPendingReceipts();
        reprintPanel.hidden = items.length === 0;
        reprintList.replaceChildren();

        items.forEach((item) => {
            const row = document.createElement('div');
            row.className = 'receipt-reprint-panel__item';

            const summary = document.createElement('div');
            const title = document.createElement('strong');
            title.textContent = describeReceipt(item.request);
            const message = document.createElement('span');
            message.textContent = item.message;
            summary.append(title, message);

            const actions = document.createElement('div');
            actions.className = 'receipt-reprint-panel__actions';

            const retryButton = document.createElement('button');
            retryButton.type = 'button';
            retryButton.className = 'btn btn-sm btn-primary';
            retryButton.dataset.receiptReprintRetry = item.key;
            retryButton.textContent = '再印刷';

            const doneButton = document.createElement('button');
            doneButton.type = 'button';
            doneButton.className = 'btn btn-sm btn-outline-secondary';
            doneButton.dataset.receiptReprintDone = item.key;
            doneButton.textContent = '完了にする';

            actions.append(retryButton, doneButton);
            row.append(summary, actions);
            reprintList.append(row);
        });
    };

    const printRequest = async (request) => {
        if (typeof window.PrinterManager !== 'function') {
            throw new Error('SII Web SDKを読み込めませんでした。ReceiptPrinter__BrowserSdkScriptUrl とネットワーク接続を確認してください。');
        }

        setStatus('SII Web SDK Serverへ領収書を送信しています。', 'info');

        const manager = new window.PrinterManager({ host: compact(config.host, 'localhost') });
        let started = false;

        try {
            await trySdkCall('start', () => manager.start({}));
            started = true;

            if (compact(config.codePage)) {
                await trySdkCall('setCodePage', () => manager.setCodePage({ codePage: config.codePage }), false);
            }

            if (compact(config.internationalCharacter)) {
                await trySdkCall(
                    'setInternationalCharacter',
                    () => manager.setInternationalCharacter({ internationalCharacter: config.internationalCharacter }),
                    false);
            }

            await trySdkCall('appendText', () => manager.appendText({ text: buildReceiptText(request) }));
            await trySdkCall('appendFeed', () => manager.appendFeed({ value: 2 }), false);
            await trySdkCall('appendCut', () => manager.appendCut({ cuttingMethod: 'partial' }), false);
            await trySdkCall('doPrint', () => manager.doPrint({}));
            removePendingReceipt(receiptKey(request));
            setStatus('領収書を印刷しました。', 'success');
        } finally {
            if (started) {
                await trySdkCall('stop', () => manager.stop({}), false);
            }
        }
    };

    reprintList?.addEventListener('click', (event) => {
        const retryButton = event.target.closest('[data-receipt-reprint-retry]');
        const doneButton = event.target.closest('[data-receipt-reprint-done]');

        if (doneButton) {
            removePendingReceipt(doneButton.dataset.receiptReprintDone);
            return;
        }

        if (!retryButton) {
            return;
        }

        const item = readPendingReceipts().find((pending) => pending.key === retryButton.dataset.receiptReprintRetry);
        if (!item) {
            renderPendingReceipts();
            return;
        }

        retryButton.disabled = true;
        void printRequest(item.request).catch((error) => {
            console.warn('SII receipt reprint failed.', error);
            upsertPendingReceipt(item.request, error.message ?? 'SII Web SDK Serverとプリンターを確認してください。');
            setStatus(`領収書を再印刷できませんでした。${error.message ?? 'SII Web SDK Serverとプリンターを確認してください。'}`, 'warning');
        }).finally(() => {
            retryButton.disabled = false;
        });
    });

    renderPendingReceipts();

    if (!requestElement) {
        return;
    }

    const initialRequest = parseRequestElement();
    void printRequest(initialRequest).catch((error) => {
        console.warn('SII receipt printing failed.', error);
        upsertPendingReceipt(initialRequest, error.message ?? 'SII Web SDK Serverとプリンターを確認してください。');
        setStatus(`領収書を印刷できませんでした。${error.message ?? 'SII Web SDK Serverとプリンターを確認してください。'}`, 'warning');
    });
})();
