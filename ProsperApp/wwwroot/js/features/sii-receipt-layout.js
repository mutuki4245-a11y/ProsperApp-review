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
        const dateTime = (value, capAtOneAm = false) => {
            const date = new Date(value);
            if (Number.isNaN(date.getTime())) return '-';
            const dateParts = Object.fromEntries(new Intl.DateTimeFormat('en-CA', {
                timeZone: 'Asia/Tokyo', year: 'numeric', month: '2-digit', day: '2-digit'
            }).formatToParts(date).filter((part) => part.type !== 'literal').map((part) => [part.type, part.value]));
            const timeParts = Object.fromEntries(new Intl.DateTimeFormat('en-CA', {
                timeZone: 'Asia/Tokyo', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
            }).formatToParts(date).filter((part) => part.type !== 'literal').map((part) => [part.type, part.value]));
            const displayDate = `${dateParts.year}/${dateParts.month}/${dateParts.day}`;
            let hour = Number(timeParts.hour);
            let minute = Number(timeParts.minute);
            if (capAtOneAm && (hour > 1 || (hour === 1 && minute > 0))) {
                hour = 1;
                minute = 0;
            }
            return `${displayDate} ${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
        };
        const addresseeName = (request) => compact(request.addressee).replace(/\s*様$/, '');
        const addresseeLine = (request) => {
            const name = addresseeName(request);
            const nameStart = Math.max(0, Math.floor((receiptWidth - textWidth(name)) / 2));
            const honorificStart = receiptWidth - textWidth('様');
            const gap = Math.max(1, honorificStart - nameStart - textWidth(name));
            return `${spaces(nameStart)}${name}${spaces(gap)}様`;
        };
        const addresseeUnderline = () => `${spaces(2)}${'-'.repeat(Math.max(16, receiptWidth - 4))}`;
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
            lines.push(addresseeUnderline());
            lines.push('');
            lines.push(twoColumnLine('発行日', dateTime(request.issued_at, true)));
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
        const describeReceipt = (request) => `${dateTime(request.issued_at, true)} / ${formatYen(request.total_amount)}`;
        const receiptKey = (request) => compact(request.checkoutId, 'unknown');
        return { buildReceiptParts, buildReceiptText, describeReceipt, formatYen, receiptKey };
    };

    window.ProsperSiiReceiptLayout = { create };
})();
