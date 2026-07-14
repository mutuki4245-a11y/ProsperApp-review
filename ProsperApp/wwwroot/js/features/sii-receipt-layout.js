(() => {
    const toAmount = (value) => Math.round(Number(value) || 0);
    const compact = (value, fallback = '') => String(value ?? fallback).trim();

    const create = (config = {}) => {
        const receiptWidth = Number.isFinite(Number(config.lineWidth)) && Number(config.lineWidth) >= 24
            ? Number(config.lineWidth)
            : 48;
        const revenueStampThreshold = 50001;
        const consumptionTaxRate = 0.10;

        const formatYen = (value) => `${toAmount(value).toLocaleString('ja-JP')}円`;
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

        const describeReceipt = (request) => {
            const table = compact(request.tableDisplayName, '-');
            const slipNo = compact(request.slipNo, request.slipId);
            return `${table} / 伝票 ${slipNo} / ${formatYen(request.totalAmount)}`;
        };

        const receiptKey = (request) => compact(request.checkoutId, `${request.slipId}:${request.closedAt}`);

        return {
            buildReceiptText,
            describeReceipt,
            formatYen,
            receiptKey
        };
    };

    window.ProsperSiiReceiptLayout = { create };
})();
