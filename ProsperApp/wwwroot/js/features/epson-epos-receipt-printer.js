(() => {
    const requestElement = document.getElementById('pendingCheckoutReceiptPrintRequest');
    if (!requestElement) {
        return;
    }

    const statusElement = document.querySelector('[data-receipt-print-status]');
    const config = window.prosperEpsonReceiptPrinter ?? {};

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

    const parseRequest = () => {
        try {
            return JSON.parse(requestElement.textContent || '{}');
        } catch (error) {
            const parseError = new Error('領収書印刷データを読み込めませんでした。');
            parseError.cause = error;
            throw parseError;
        }
    };

    const escapeXml = (value) => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&apos;');

    const xmlAttributes = (attributes) => Object.entries(attributes)
        .filter(([, value]) => value !== undefined && value !== null && value !== '')
        .map(([name, value]) => ` ${name}="${escapeXml(value)}"`)
        .join('');

    const textElement = (text, attributes = {}) =>
        `<text${xmlAttributes(attributes)}>${escapeXml(text)}</text>`;

    const normalizeEndpointPath = (value) => {
        const path = compact(value, '/cgi-bin/epos/service.cgi');
        return path.startsWith('/') ? path : `/${path}`;
    };

    const toPositiveInteger = (value, fallback) => {
        const numeric = Number(value);
        return Number.isFinite(numeric) && numeric > 0 ? Math.trunc(numeric) : fallback;
    };

    const buildEndpoint = () => {
        const address = compact(config.printerAddress);
        if (!address) {
            throw new Error('EPSON TM-T88VIIの接続先が未設定です。ReceiptPrinter__PrinterAddress を設定してください。');
        }

        const endpointPath = normalizeEndpointPath(config.endpointPath);
        const timeout = Math.min(Math.max(toPositiveInteger(config.timeoutMilliseconds, 10000), 1000), 120000);
        const deviceId = compact(config.deviceId, 'local_printer');
        const url = /^https?:\/\//i.test(address)
            ? new URL(address)
            : new URL(`${config.useHttps === false ? 'http' : 'https'}://${address}`);

        if (url.pathname === '/' || !url.pathname) {
            url.pathname = endpointPath;
        }

        url.searchParams.set('devid', deviceId);
        url.searchParams.set('timeout', String(timeout));

        if (window.location.protocol === 'https:' && url.protocol === 'http:') {
            throw new Error('HTTPSの画面からHTTPのePOS endpointへはブラウザが送信をブロックします。TM-T88VII側のHTTPSを有効にし、ReceiptPrinter__UseHttps=true を設定してください。');
        }

        return { url, timeout };
    };

    const buildEposPrintXml = (request) => {
        const textLang = compact(config.textLang, 'mul');
        const printBody = [
            '<epos-print xmlns="http://www.epson-pos.com/schemas/2011/03/epos-print">',
            textElement(buildReceiptText(request), { lang: textLang, align: 'left' }),
            '<feed line="2" />',
            '<cut type="feed" />',
            '</epos-print>'
        ].join('');

        return [
            '<?xml version="1.0" encoding="UTF-8"?>',
            '<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">',
            '<s:Body>',
            printBody,
            '</s:Body>',
            '</s:Envelope>'
        ].join('');
    };

    const findResponseElement = (document) =>
        Array.from(document.getElementsByTagName('*')).find((element) => element.localName === 'response');

    const assertEposResponse = (responseXml) => {
        const document = new DOMParser().parseFromString(responseXml, 'application/xml');
        const parserError = document.getElementsByTagName('parsererror')[0];
        if (parserError) {
            throw new Error('EPSON ePOSの応答XMLを読み取れませんでした。');
        }

        const responseElement = findResponseElement(document);
        if (!responseElement) {
            throw new Error('EPSON ePOSの印刷結果が応答に含まれていません。');
        }

        const success = responseElement.getAttribute('success');
        if (success === 'true' || success === '1') {
            return;
        }

        const code = compact(responseElement.getAttribute('code'), 'unknown');
        const status = compact(responseElement.getAttribute('status'), 'unknown');
        throw new Error(`EPSON ePOS印刷に失敗しました。code=${code}, status=${status}`);
    };

    const postEposPrintXml = async (endpoint, xml) => {
        const controller = new AbortController();
        const abortTimer = window.setTimeout(() => controller.abort(), endpoint.timeout + 5000);

        try {
            const response = await fetch(endpoint.url.toString(), {
                method: 'POST',
                mode: 'cors',
                cache: 'no-store',
                headers: {
                    'Content-Type': 'text/xml; charset=UTF-8',
                    'SOAPAction': '""'
                },
                body: xml,
                signal: controller.signal
            });

            if (!response.ok) {
                throw new Error(`EPSON ePOS endpointがHTTP ${response.status}を返しました。`);
            }

            assertEposResponse(await response.text());
        } catch (error) {
            if (error?.name === 'AbortError') {
                throw new Error('EPSON ePOS印刷がタイムアウトしました。');
            }

            if (error instanceof TypeError) {
                throw new Error('EPSON TM-T88VIIへ接続できませんでした。同一ネットワーク、ePOS-Print有効化、HTTPS証明書、CORSを確認してください。');
            }

            throw error;
        } finally {
            window.clearTimeout(abortTimer);
        }
    };

    const printReceipt = async () => {
        setStatus('EPSON TM-T88VIIへ領収書を送信しています。', 'info');

        const request = parseRequest();
        const endpoint = buildEndpoint();
        const xml = buildEposPrintXml(request);

        await postEposPrintXml(endpoint, xml);
        setStatus('領収書を印刷しました。', 'success');
    };

    void printReceipt().catch((error) => {
        console.warn('EPSON ePOS receipt printing failed.', error);
        setStatus(`領収書を印刷できませんでした。${error.message ?? 'EPSON TM-T88VIIとePOS設定を確認してください。'}`, 'warning');
    });
})();
