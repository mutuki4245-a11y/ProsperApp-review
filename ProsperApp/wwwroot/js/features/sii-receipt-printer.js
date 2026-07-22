(() => {
    const statusElement = document.querySelector('[data-receipt-print-status]');
    const reprintPanel = document.querySelector('[data-receipt-reprint-panel]');
    const reprintList = document.querySelector('[data-receipt-reprint-list]');
    const config = window.prosperSiiReceiptPrinter ?? {};
    const sdkScript = window.prosperSiiReceiptSdkScript ?? {};
    const pendingStorageKey = 'prosper:receipt-reprints:v1';
    const successStorageKey = 'prosper:receipt-print-success:v1';
    const compact = (value, fallback = '') => String(value ?? fallback).trim();

    const setStatus = (message, state) => {
        if (!statusElement) {
            return;
        }

        statusElement.hidden = false;
        statusElement.textContent = message;
        statusElement.classList.remove('alert-info', 'alert-success', 'alert-warning');
        statusElement.classList.add(state === 'success' ? 'alert-success' : state === 'warning' ? 'alert-warning' : 'alert-info');
    };

    const formatErrorMessage = (error, fallback = 'SII Web SDK Serverとプリンターを確認してください。') => {
        const message = compact(error?.message, fallback);
        const code = compact(error?.code);
        return code ? `${message} code=${code}` : message;
    };

    const createFallbackLayout = () => ({
        buildReceiptText: () => {
            throw new Error('領収書レイアウト処理を読み込めませんでした。ページを再読み込みしてください。');
        },
        describeReceipt: (request) => {
            const table = compact(request?.tableDisplayName, '-');
            const slipNo = compact(request?.slipNo, request?.slipId);
            const amount = Number(request?.totalAmount || 0).toLocaleString('ja-JP');
            return `${table} / 伝票 ${slipNo} / ${amount}円`;
        },
        receiptKey: (request) => compact(request?.checkoutId, `${request?.slipId ?? 'unknown'}:${request?.closedAt ?? Date.now()}`)
    });

    const createReceiptLayout = () => {
        if (typeof window.ProsperSiiReceiptLayout?.create !== 'function') {
            throw new Error('領収書レイアウトJSを読み込めませんでした。');
        }

        return window.ProsperSiiReceiptLayout.create(config);
    };

    let receiptLayout = createFallbackLayout();
    try {
        receiptLayout = createReceiptLayout();
    } catch (error) {
        console.warn('SII receipt layout initialization failed.', error);
        setStatus(`領収書印刷を開始できませんでした。${formatErrorMessage(error)}`, 'warning');
    }

    const resolvePrinterManager = () => {
        if (typeof window.PrinterManager === 'function') {
            return window.PrinterManager;
        }

        try {
            if (typeof PrinterManager === 'function') {
                return PrinterManager;
            }
        } catch {
        }

        return null;
    };

    const describeSdkUnavailable = () => {
        if (sdkScript.failed === true) {
            return `SII Web SDKのscript取得に失敗しました。url=${compact(sdkScript.url, '(未設定)')}`;
        }

        if (compact(sdkScript.url).length === 0) {
            return 'SII Web SDKのscript URLが未設定です。ReceiptPrinter__BrowserSdkScriptUrl を確認してください。';
        }

        return `SII Web SDKを利用できませんでした。script URL、ネットワーク接続、SDKの読み込み状態を確認してください。url=${compact(sdkScript.url)}`;
    };

    const sdkStepLabel = (label) => ({
        start: 'SII Web SDK Server接続',
        setCodePage: 'コードページ設定',
        setInternationalCharacter: '国際文字設定',
        appendText: '印字データ送信',
        appendFeed: '紙送り',
        appendCut: 'カット指定',
        doPrint: '印刷実行',
        stop: 'SII Web SDK Server切断'
    }[label] ?? label);

    const assertSdkResult = (label, result) => {
        if (!result || Number(result.errorCode) !== 0) {
            const detail = result
                ? `errorCode=${result.errorCode}, errorString=${result.errorString ?? ''}, errorExtendedString=${result.errorExtendedString ?? ''}`
                : 'no result';
            throw new Error(`${sdkStepLabel(label)}に失敗しました。${detail}`);
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

    const appendTotalImage = async (manager, request) => {
        if (typeof manager.appendImage !== 'function' || typeof window.ProsperSiiPrintImage?.createTotal !== 'function') {
            throw new Error('領収金額の画像印字を利用できません。ページを再読み込みしてください。');
        }
        const image = await window.ProsperSiiPrintImage.createTotal({
            label: '領収金額', amount: request.total_amount, lineWidth: config.lineWidth
        });
        await trySdkCall('領収金額画像送信', () => manager.appendImage({ data: image }));
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

    const readSuccessfulReceipts = () => {
        try {
            const stored = JSON.parse(localStorage.getItem(successStorageKey) || '[]');
            return Array.isArray(stored) ? stored.filter((value) => compact(value).length > 0) : [];
        } catch {
            return [];
        }
    };

    const markSuccessfulReceipt = (request) => {
        const key = receiptKey(request);
        if (!key || key === 'unknown') return;
        const values = [key, ...readSuccessfulReceipts().filter((value) => value !== key)].slice(0, 200);
        try { localStorage.setItem(successStorageKey, JSON.stringify(values)); } catch { }
    };

    const hasSuccessfulReceipt = (request) => readSuccessfulReceipts().includes(receiptKey(request));

    const clearReceiptTerminalState = (checkoutId) => {
        const value = compact(checkoutId);
        if (!value) return;
        writePendingReceipts(readPendingReceipts().filter((item) => item.key !== value));
        try {
            localStorage.setItem(successStorageKey, JSON.stringify(readSuccessfulReceipts().filter((key) => key !== value)));
        } catch { }
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
        const PrinterManagerClass = resolvePrinterManager();
        if (PrinterManagerClass === null) {
            throw new Error(describeSdkUnavailable());
        }

        setStatus('SII Web SDK Serverへ領収書を送信しています。', 'info');

        const manager = new PrinterManagerClass({ host: compact(config.host, 'localhost') });
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

            const parts = typeof receiptLayout.buildReceiptParts === 'function'
                ? receiptLayout.buildReceiptParts(request)
                : { beforeTotal: receiptLayout.buildReceiptText(request), total: '', afterTotal: '' };
            await trySdkCall('appendText', () => manager.appendText({ text: parts.beforeTotal }));
            await appendTotalImage(manager, request);
            if (parts.afterTotal) {
                await trySdkCall('appendText', () => manager.appendText({ text: parts.afterTotal }));
            }
            await trySdkCall('appendFeed', () => manager.appendFeed({ value: 2 }), false);
            await trySdkCall('appendCut', () => manager.appendCut({ cuttingMethod: 'partial' }), false);
            await trySdkCall('doPrint', () => manager.doPrint({}));
            markSuccessfulReceipt(request);
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
            const message = formatErrorMessage(error);
            upsertPendingReceipt(item.request, message);
            setStatus(`領収書を再印刷できませんでした。${message}`, 'warning');
        }).finally(() => {
            retryButton.disabled = false;
        });
    });

    renderPendingReceipts();

    const print = async (request, options = {}) => {
        const explicitReprint = options.explicitReprint !== false;
        const prepared = {
            ...request,
            isRetry: Boolean(request?.isRetry || (explicitReprint && hasSuccessfulReceipt(request)))
        };
        try {
            await printRequest(prepared);
        } catch (error) {
            upsertPendingReceipt(prepared, formatErrorMessage(error));
            throw error;
        }
    };

    window.ProsperSiiReceiptPrinterApi = { print, clearReceiptTerminalState };
})();
