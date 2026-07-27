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
        const addresseeName = (request) => compact(request.addressee).replace(/\s*様$/, '');
        const addresseeLine = (request) => {
            const name = addresseeName(request);
            const underlineLength = Math.max(16, receiptWidth - textWidth(name) - textWidth('様') - 8);
            return twoColumnLine('', `${name}${'-'.repeat(underlineLength)}様`);
        };
        const stampBox = () => {
            const innerWidth = 16;
            const innerRows = 5;
            const edge = `+${'-'.repeat(innerWidth)}+`;
            const inside = `|${' '.repeat(innerWidth)}|`;
            return [edge, ...Array.from({ length: innerRows }, () => inside), edge];
        };
        const issuerText = (issuer, property) => compact(issuer?.[property], property === 'logo' ? '' : '未設定');
        const buildReceiptParts = (request) => {
            const lines = [];
            const issuer = request.issuer || {};
            const totalAmount = toAmount(request.total_amount);
            lines.push(issuerText(issuer, 'logo'));
            lines.push(issuerText(issuer, 'store_name'));
            lines.push(issuerText(issuer, 'address'));
            lines.push(`TEL ${issuerText(issuer, 'phone')}`);
            lines.push(`登録番号 ${issuerText(issuer, 'invoice_registration_number')}`);
            lines.push(centerLine('領収書'));
            if (request.isRetry) lines.push(centerLine('再試行'));
            lines.push(separator());
            lines.push('');
            lines.push('');
            lines.push(addresseeLine(request));
            lines.push('');
            lines.push(twoColumnLine('発行日', dateTime(request.issued_at)));
            lines.push('');
            lines.push(centerLine(compact(request.particulars, 'ご飲食代として')));
            lines.push('');
            const beforeTotal = `${lines.join('\n')}\n`;
            const total = `${twoColumnLine('領収金額', formatYen(totalAmount))}\n`;
            const afterTotal = [];
            afterTotal.push(twoColumnLine('', `（内消費税額 ${formatYen(request.consumption_tax_amount)}）`));
            const payments = Array.isArray(request.payments) ? request.payments : [];
            afterTotal.push('支払い方法：');
            if (payments.length === 0) {
                afterTotal.push(twoColumnLine('', '（請求なし 0円）'));
            } else {
                payments.forEach((payment) => afterTotal.push(twoColumnLine('', `（${compact(payment.method_name, '支払い')} ${formatYen(payment.amount)}）`)));
            }
            if (totalAmount >= 55000) {
                afterTotal.push('');
                afterTotal.push('収入印紙欄');
                afterTotal.push(...stampBox());
            }
            afterTotal.push(separator());
            afterTotal.push('');
            return { beforeTotal, total, afterTotal: `${afterTotal.join('\n')}\n` };
        };
        const buildReceiptText = (request) => {
            const { beforeTotal, total, afterTotal } = buildReceiptParts(request);
            return `${beforeTotal}${total}${afterTotal}`;
        };
        const describeReceipt = (request) => `${dateTime(request.issued_at)} / ${formatYen(request.total_amount)}`;
        const receiptKey = (request) => compact(request.checkoutId, 'unknown');
        return { buildReceiptParts, buildReceiptText, describeReceipt, formatYen, receiptKey };
    };

    window.ProsperSiiReceiptLayout = { create };
})();
