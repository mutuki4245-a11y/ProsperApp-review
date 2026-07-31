(() => {
    const statusElement = document.querySelector('[data-receipt-print-status]');
    const reprintPanel = document.querySelector('[data-receipt-reprint-panel]');
    const reprintList = document.querySelector('[data-receipt-reprint-list]');
    const config = window.prosperSiiReceiptPrinter ?? {};
    const sdkScript = window.prosperSiiReceiptSdkScript ?? {};
    const pendingStorageKey = 'prosper:receipt-reprints:v1';
    const successStorageKey = 'prosper:receipt-print-success:v1';
    const compact = (value, fallback = '') => String(value ?? fallback).trim();
    const ESC = 0x1b;
    const GS = 0x1d;

    const toPositiveInteger = (value, fallback) => {
        const number = Number(value);
        return Number.isFinite(number) && number > 0 ? Math.floor(number) : fallback;
    };
    const toInteger = (value, fallback) => {
        const number = Number(value);
        return Number.isFinite(number) ? Math.floor(number) : fallback;
    };

    const printableWidthDots = () => {
        const configured = toPositiveInteger(config.printableWidthDots, 0);
        if (configured > 0) return configured;
        return toPositiveInteger(config.paperWidthMillimeters, 80) <= 58 ? 432 : 576;
    };

    const logoMaxWidthDots = () => Math.min(
        printableWidthDots(),
        toPositiveInteger(config.logoMaxWidthDots, Math.min(384, printableWidthDots())));
    const logoMaxHeightDots = () => toPositiveInteger(config.logoMaxHeightDots, 160);
    const logoThreshold = () => Math.min(255, Math.max(0, toInteger(config.logoThreshold, 180)));
    const commandBlob = (bytes) => new Blob(
        [bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes)],
        { type: 'application/octet-binary' });
    const concatBytes = (...chunks) => {
        const arrays = chunks.map((chunk) => chunk instanceof Uint8Array ? chunk : new Uint8Array(chunk));
        const output = new Uint8Array(arrays.reduce((total, chunk) => total + chunk.length, 0));
        let offset = 0;
        arrays.forEach((chunk) => {
            output.set(chunk, offset);
            offset += chunk.length;
        });
        return output;
    };
    const alignmentByte = (alignment) => ({
        center: 1,
        right: 2
    }[compact(alignment).toLowerCase()] ?? 0);
    const characterSizeByte = (width = 1, height = 1) => {
        const widthMultiplier = Math.min(8, Math.max(1, toPositiveInteger(width, 1)));
        const heightMultiplier = Math.min(8, Math.max(1, toPositiveInteger(height, 1)));
        return ((widthMultiplier - 1) << 4) | (heightMultiplier - 1);
    };
    const textModeCommand = (style = {}) => new Uint8Array([
        ESC, 0x61, alignmentByte(style.alignment),
        GS, 0x21, characterSizeByte(style.width, style.height),
        ESC, 0x45, style.bold ? 1 : 0
    ]);
    const normalizeImageSource = (source) => {
        const value = compact(source);
        if (value.startsWith('~/')) {
            return `${window.location.origin}/${value.slice(2)}`;
        }

        return value;
    };
    const loadImageElement = async (source) => {
        if (typeof document === 'undefined' || typeof document.createElement !== 'function') {
            throw new Error('ロゴ画像を処理できるブラウザ環境ではありません。');
        }

        const normalized = normalizeImageSource(source);
        const response = await fetch(normalized, { cache: 'force-cache' });
        if (!response.ok) {
            throw new Error(`ロゴ画像を取得できませんでした。status=${response.status}`);
        }

        const blob = await response.blob();
        const objectUrl = URL.createObjectURL(blob);
        const image = new Image();
        const loaded = new Promise((resolve, reject) => {
            image.onload = () => resolve();
            image.onerror = () => reject(new Error('ロゴ画像を読み込めませんでした。'));
        });
        image.src = objectUrl;
        if (typeof image.decode === 'function') {
            await image.decode().catch(() => loaded);
        } else {
            await loaded;
        }

        return {
            image,
            release: () => URL.revokeObjectURL(objectUrl)
        };
    };
    const createRasterBitImageCommand = (bytesPerRow, height, rasterData) => {
        const xL = bytesPerRow & 0xff;
        const xH = (bytesPerRow >> 8) & 0xff;
        const yL = height & 0xff;
        const yH = (height >> 8) & 0xff;
        return concatBytes([GS, 0x76, 0x30, 0x00, xL, xH, yL, yH], rasterData);
    };
    const rasterizeLogo = async (source) => {
        const { image, release } = await loadImageElement(source);
        try {
            const naturalWidth = image.naturalWidth || image.width;
            const naturalHeight = image.naturalHeight || image.height;
            if (!naturalWidth || !naturalHeight) {
                throw new Error('ロゴ画像のサイズを取得できませんでした。');
            }

            const scale = Math.min(1, logoMaxWidthDots() / naturalWidth, logoMaxHeightDots() / naturalHeight);
            const width = Math.max(1, Math.round(naturalWidth * scale));
            const height = Math.max(1, Math.round(naturalHeight * scale));
            const canvas = document.createElement('canvas');
            canvas.width = width;
            canvas.height = height;
            const context = canvas.getContext('2d', { willReadFrequently: true });
            if (!context) {
                throw new Error('ロゴ画像をラスター化できませんでした。');
            }

            context.fillStyle = '#ffffff';
            context.fillRect(0, 0, width, height);
            context.drawImage(image, 0, 0, width, height);
            const pixels = context.getImageData(0, 0, width, height).data;
            const bytesPerRow = Math.ceil(width / 8);
            const rasterData = new Uint8Array(bytesPerRow * height);
            const threshold = logoThreshold();
            for (let y = 0; y < height; y += 1) {
                for (let x = 0; x < width; x += 1) {
                    const pixelOffset = (y * width + x) * 4;
                    const alpha = pixels[pixelOffset + 3];
                    const luminance = (pixels[pixelOffset] * 0.299) +
                        (pixels[pixelOffset + 1] * 0.587) +
                        (pixels[pixelOffset + 2] * 0.114);
                    if (alpha > 127 && luminance < threshold) {
                        rasterData[(y * bytesPerRow) + Math.floor(x / 8)] |= 0x80 >> (x % 8);
                    }
                }
            }

            return createRasterBitImageCommand(bytesPerRow, height, rasterData);
        } finally {
            release();
        }
    };

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
        appendBinary: '印字制御コマンド送信',
        appendLogo: 'ロゴ画像送信',
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

    const appendBinaryBytes = (manager, bytes, label = 'appendBinary') =>
        trySdkCall(label, () => manager.appendBinary({ data: commandBlob(bytes) }));

    const appendTextSection = async (manager, section) => {
        const text = compact(section?.text);
        if (!text) {
            return;
        }

        // RP-F10 is ESC/POS compatible. GS ! selects character width/height magnification.
        await appendBinaryBytes(manager, textModeCommand(section));
        await trySdkCall('appendText', () => manager.appendText({ text: section.text }));
        await appendBinaryBytes(manager, textModeCommand());
    };

    const appendRasterLogo = async (manager, section) => {
        const source = compact(section?.source);
        if (!source) {
            return;
        }

        // GS v 0 prints the following 1-bit raster image data.
        const rasterCommand = await rasterizeLogo(source);
        await appendBinaryBytes(manager, textModeCommand({ alignment: section.alignment || 'center' }), 'appendLogo');
        await appendBinaryBytes(manager, rasterCommand, 'appendLogo');
        await appendBinaryBytes(manager, textModeCommand());
        await trySdkCall('appendFeed', () => manager.appendFeed({ value: 1 }), false);
    };

    const appendReceiptSections = async (manager, request) => {
        if (typeof receiptLayout.buildReceiptPrintPlan === 'function') {
            const sections = receiptLayout.buildReceiptPrintPlan(request);
            for (const section of sections) {
                if (section?.type === 'image') {
                    await appendRasterLogo(manager, section);
                } else if (section?.type === 'text') {
                    await appendTextSection(manager, section);
                }
            }
            return;
        }

        const parts = typeof receiptLayout.buildReceiptParts === 'function'
            ? receiptLayout.buildReceiptParts(request)
            : { beforeTotal: receiptLayout.buildReceiptText(request), total: '', afterTotal: '' };
        await appendTextSection(manager, { text: parts.beforeTotal });
        if (parts.total) {
            await appendTextSection(manager, { text: parts.total, alignment: 'center', width: 2, height: 2, bold: true });
        }
        if (parts.afterTotal) {
            await appendTextSection(manager, { text: parts.afterTotal });
        }
    };

    const enqueuePrint = (job) => typeof window.ProsperSiiPrintQueue?.enqueue === 'function'
        ? window.ProsperSiiPrintQueue.enqueue(job)
        : job();

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

            await appendReceiptSections(manager, request);
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
        void enqueuePrint(() => printRequest(item.request)).catch((error) => {
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
            await enqueuePrint(() => printRequest(prepared));
        } catch (error) {
            upsertPendingReceipt(prepared, formatErrorMessage(error));
            throw error;
        }
    };

    window.ProsperSiiReceiptPrinterApi = { print, clearReceiptTerminalState };
})();
