(() => {
    const requestElement = document.getElementById('pendingCheckoutReceiptPrintRequest');
    if (!requestElement) {
        return;
    }

    const statusElement = document.querySelector('[data-receipt-print-status]');
    const config = window.prosperSiiReceiptPrinter ?? {};

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

    const receiptWidth = 48;
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

    const parseRequest = () => {
        try {
            return JSON.parse(requestElement.textContent || '{}');
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

    const printReceipt = async () => {
        if (typeof window.PrinterManager !== 'function') {
            throw new Error('SII Web SDKを読み込めませんでした。');
        }

        const request = parseRequest();
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
            setStatus('領収書を印刷しました。', 'success');
        } finally {
            if (started) {
                await trySdkCall('stop', () => manager.stop({}), false);
            }
        }
    };

    void printReceipt().catch((error) => {
        console.warn('SII receipt printing failed.', error);
        setStatus('領収書を印刷できませんでした。SII Web SDK Serverとプリンターを確認してください。', 'warning');
    });
})();
