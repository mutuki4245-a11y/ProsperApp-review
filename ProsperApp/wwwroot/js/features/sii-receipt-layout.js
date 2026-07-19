(() => {
    const toAmount = (value) => Math.round(Number(value) || 0);
    const compact = (value, fallback = '') => String(value ?? fallback).trim();

    const create = (config = {}) => {
        const receiptWidth = Number.isFinite(Number(config.lineWidth)) && Number(config.lineWidth) >= 24
            ? Number(config.lineWidth)
            : 48;
        const formatYen = (value) => `${toAmount(value).toLocaleString('ja-JP')}円`;
        const charWidth = (char) => (char.codePointAt(0) ?? 0) > 0x00ff ? 2 : 1;
        const textWidth = (text) => Array.from(String(text)).reduce((total, char) => total + charWidth(char), 0);
        const spaces = (count) => ' '.repeat(Math.max(0, count));
        const separator = () => '-'.repeat(receiptWidth);
        const centerLine = (text) => `${spaces(Math.floor((receiptWidth - textWidth(text)) / 2))}${text}`;
        const twoColumnLine = (left, right) => `${left}${spaces(receiptWidth - textWidth(left) - textWidth(right))}${right}`;
        const dateTime = (value) => new Intl.DateTimeFormat('ja-JP', {
            year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit'
        }).format(new Date(value));
        const addresseeText = (request) => {
            const value = compact(request.addressee);
            return value.endsWith('様') ? value : value ? `${value} 様` : '様';
        };
        const issuerText = (issuer, property) => compact(issuer?.[property], property === 'logo' ? '' : '未設定');
        const buildReceiptText = (request) => {
            const lines = [];
            const issuer = request.issuer || {};
            const totalAmount = toAmount(request.total_amount);
            lines.push(issuerText(issuer, 'logo'));
            lines.push(issuerText(issuer, 'company_name'));
            lines.push(issuerText(issuer, 'store_name'));
            lines.push(issuerText(issuer, 'address'));
            lines.push(`TEL ${issuerText(issuer, 'phone')}`);
            lines.push(`登録番号 ${issuerText(issuer, 'invoice_registration_number')}`);
            lines.push(centerLine('領収書'));
            if (request.isRetry) lines.push(centerLine('再試行'));
            lines.push(separator());
            lines.push(twoColumnLine('宛名', addresseeText(request)));
            lines.push(twoColumnLine('発行日', dateTime(request.issued_at)));
            lines.push('');
            lines.push(centerLine(compact(request.particulars, 'ご飲食代として')));
            lines.push('');
            lines.push(twoColumnLine('領収金額', formatYen(totalAmount)));
            lines.push(twoColumnLine('10%対象', formatYen(request.taxable_amount_including_tax)));
            lines.push(twoColumnLine('内消費税額', formatYen(request.consumption_tax_amount)));
            const payments = Array.isArray(request.payments) ? request.payments : [];
            if (payments.length === 0) {
                lines.push(twoColumnLine('支払い方法', '請求なし 0円'));
            } else {
                payments.forEach((payment) => lines.push(twoColumnLine(compact(payment.method_name, '支払い'), formatYen(payment.amount))));
            }
            if (totalAmount >= 55000) {
                lines.push(''); lines.push('収入印紙欄'); lines.push('+------------------------------+'); lines.push('|                              |'); lines.push('|                              |'); lines.push('+------------------------------+');
            }
            lines.push('');
            lines.push('担当者印');
            lines.push('+--------------------+');
            lines.push('|                    |');
            lines.push('|                    |');
            lines.push('|                    |');
            lines.push('+--------------------+');
            lines.push(separator());
            lines.push('');
            return `${lines.join('\n')}\n`;
        };
        const describeReceipt = (request) => `${dateTime(request.issued_at)} / ${formatYen(request.total_amount)}`;
        const receiptKey = (request) => compact(request.checkoutId, 'unknown');
        return { buildReceiptText, describeReceipt, formatYen, receiptKey };
    };

    window.ProsperSiiReceiptLayout = { create };
})();
