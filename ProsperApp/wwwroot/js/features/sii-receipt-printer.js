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

    const formatDateTime = (value) => {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return compact(value);
        }

        return new Intl.DateTimeFormat('ja-JP', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit'
        }).format(date);
    };

    const appendAmountLine = (lines, label, amount) => {
        if (toAmount(amount) !== 0) {
            lines.push(`${label}: ${formatYen(amount)}`);
        }
    };

    const buildReceiptText = (request) => {
        const lines = [];
        lines.push(compact(request.storeName, '店舗'));
        lines.push('領収書');
        lines.push('------------------------------');
        lines.push(`伝票: ${compact(request.slipNo, String(request.slipId ?? ''))}`);
        lines.push(`卓: ${compact(request.tableDisplayName, '-')}`);
        lines.push(`日時: ${formatDateTime(request.closedAt)}`);
        lines.push('------------------------------');

        const detailLines = Array.isArray(request.lines) ? request.lines : [];
        if (detailLines.length === 0) {
            lines.push('明細なし');
        } else {
            detailLines.forEach((line) => {
                const name = compact(line.name, '明細');
                const quantity = Number(line.quantity) || 0;
                lines.push(name);
                lines.push(`  ${quantity.toLocaleString('ja-JP')} x ${formatYen(line.unitPrice)} = ${formatYen(line.amount)}`);
            });
        }

        lines.push('------------------------------');
        appendAmountLine(lines, '小計', request.subtotalAmount);
        appendAmountLine(lines, 'サービス料', request.serviceTaxAmount);
        appendAmountLine(lines, '指名', request.nominationAmount);
        appendAmountLine(lines, 'カラオケ', request.karaokeAmount);
        appendAmountLine(lines, '調整', request.adjustmentAmount);
        lines.push(`合計: ${formatYen(request.totalAmount)}`);

        const payments = Array.isArray(request.payments) ? request.payments : [];
        if (payments.length > 0) {
            lines.push('------------------------------');
            payments.forEach((payment) => {
                lines.push(`${compact(payment.methodName, payment.methodCode)}: ${formatYen(payment.amount)}`);
            });
        }

        if (request.receivedAmount !== null && request.receivedAmount !== undefined) {
            lines.push(`お預り: ${formatYen(request.receivedAmount)}`);
            lines.push(`お釣り: ${formatYen(request.changeAmount)}`);
        }

        lines.push('');
        lines.push('ありがとうございました');
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
