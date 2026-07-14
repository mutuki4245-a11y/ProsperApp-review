(() => {
    const requestElement = document.getElementById('pendingCheckoutReceiptPrintRequest');
    const statusElement = document.querySelector('[data-receipt-print-status]');
    const reprintPanel = document.querySelector('[data-receipt-reprint-panel]');
    const reprintList = document.querySelector('[data-receipt-reprint-list]');
    const config = window.prosperSiiReceiptPrinter ?? {};
    const receiptLayout = window.ProsperSiiReceiptLayout.create(config);
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

    const compact = (value, fallback = '') => String(value ?? fallback).trim();

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

    const receiptKey = (request) => receiptLayout.receiptKey(request);

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
        return receiptLayout.describeReceipt(request);
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

            await trySdkCall('appendText', () => manager.appendText({ text: receiptLayout.buildReceiptText(request) }));
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
