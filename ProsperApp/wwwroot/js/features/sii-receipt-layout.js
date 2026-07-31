(() => {
    const toAmount = (value) => Math.round(Number(value) || 0);
    const compact = (value, fallback = '') => String(value ?? fallback).trim();
    const imageSourcePattern = /^(data:image\/|https?:\/\/|\/(?!\/)|~\/|\.{0,2}\/|[^?\s]+\.(?:bmp|png|jpe?g|gif|webp|svg)(?:[?#].*)?$)/i;

    const create = (config = {}) => {
        const receiptWidth = Number.isFinite(Number(config.lineWidth)) && Number(config.lineWidth) >= 24
            ? Number(config.lineWidth)
            : 48;
        const formatYen = (value) => `${toAmount(value).toLocaleString('ja-JP')}円`;
        const formatReceiptTotal = (value) => `¥ ${toAmount(value).toLocaleString('ja-JP')} -`;
        const charWidth = (char) => (char.codePointAt(0) ?? 0) > 0x00ff ? 2 : 1;
        const textWidth = (text) => Array.from(String(text)).reduce((total, char) => total + charWidth(char), 0);
        const spaces = (count) => ' '.repeat(Math.max(0, count));
        const separator = () => '-'.repeat(receiptWidth);
        const centerLine = (text) => `${spaces(Math.floor((receiptWidth - textWidth(text)) / 2))}${text}`;
        const twoColumnLine = (left, right) => `${left}${spaces(receiptWidth - textWidth(left) - textWidth(right))}${right}`;
        const isImageSource = (value) => imageSourcePattern.test(compact(value));
        const firstImageSource = (...values) => values.map((value) => compact(value)).find(isImageSource) || '';
        const normalizedLogoText = (issuer) => {
            const value = compact(issuer?.logo);
            return value && !isImageSource(value) ? value : '';
        };
        const logoImageSource = (request, issuer) => firstImageSource(
            issuer?.logo,
            issuer?.logo_url,
            issuer?.logoImageUrl,
            issuer?.logo_image_url,
            request?.logoImageUrl,
            config.logoImageUrl);
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
        const issuedDate = (value) => {
            const date = new Date(value);
            if (Number.isNaN(date.getTime())) return '-';
            const parts = Object.fromEntries(new Intl.DateTimeFormat('ja-JP', {
                timeZone: 'Asia/Tokyo',
                year: 'numeric',
                month: 'numeric',
                day: 'numeric',
                weekday: 'short'
            }).formatToParts(date).filter((part) => part.type !== 'literal').map((part) => [part.type, part.value]));
            return `${parts.year}年 ${parts.month}月 ${parts.day}日（${parts.weekday}）`;
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
        const issuerLines = (issuer) => {
            const companyName = issuerText(issuer, 'company_name');
            const storeName = issuerText(issuer, 'store_name');
            const lines = [];
            if (companyName !== storeName) {
                lines.push(companyName);
            }
            lines.push(storeName);
            lines.push(issuerText(issuer, 'address'));
            lines.push(`TEL ${issuerText(issuer, 'phone')}`);
            lines.push(`登録番号 ${issuerText(issuer, 'invoice_registration_number')}`);
            return lines;
        };
        const buildReceiptContent = (request) => {
            const issuer = request.issuer || {};
            const totalAmount = toAmount(request.total_amount);
            const logoSource = logoImageSource(request, issuer);
            const logoText = normalizedLogoText(issuer);
            const headerLines = [];
            if (logoText) {
                headerLines.push(centerLine(logoText), '');
            }
            headerLines.push(centerLine('領収書'));
            if (request.isRetry) headerLines.push(centerLine('再試行'));
            headerLines.push(centerLine(issuedDate(request.issued_at)));
            headerLines.push('');
            headerLines.push('');
            headerLines.push(addresseeLine(request));
            headerLines.push(addresseeUnderline());
            headerLines.push('');
            headerLines.push(centerLine('領収金額'));

            const afterTotal = [''];
            afterTotal.push(centerLine(compact(request.particulars, 'ご飲食代として')));
            afterTotal.push(centerLine('上記 正に領収致しました。'));
            afterTotal.push('');
            afterTotal.push(twoColumnLine('10%対象', formatYen(request.taxable_amount_including_tax ?? totalAmount)));
            afterTotal.push(twoColumnLine('内消費税額', formatYen(request.consumption_tax_amount)));
            const payments = Array.isArray(request.payments) ? request.payments : [];
            afterTotal.push('支払い方法');
            if (payments.length === 0) {
                afterTotal.push(twoColumnLine('', '請求なし 0円'));
            } else {
                payments.forEach((payment) => afterTotal.push(twoColumnLine('', `${compact(payment.method_name, '支払い')} ${formatYen(payment.amount)}`)));
            }
            if (totalAmount >= 55000) {
                afterTotal.push('');
                afterTotal.push('収入印紙欄');
                afterTotal.push(...stampBox());
            }
            afterTotal.push(separator());
            afterTotal.push('');
            afterTotal.push(...issuerLines(issuer));
            afterTotal.push('');
            return {
                logoImageSource: logoSource,
                logoText,
                headerLines,
                totalLine: centerLine(formatReceiptTotal(totalAmount)),
                totalPrintText: formatReceiptTotal(totalAmount),
                afterTotal
            };
        };
        const buildReceiptParts = (request) => {
            const content = buildReceiptContent(request);
            return {
                beforeTotal: `${content.headerLines.join('\n')}\n`,
                total: `${content.totalLine}\n`,
                afterTotal: `${content.afterTotal.join('\n')}\n`
            };
        };
        const buildReceiptText = (request) => {
            const { beforeTotal, total, afterTotal } = buildReceiptParts(request);
            return `${beforeTotal}${total}${afterTotal}`;
        };
        const buildReceiptPrintPlan = (request) => {
            const content = buildReceiptContent(request);
            const sections = [];
            if (content.logoImageSource) {
                sections.push({ type: 'image', source: content.logoImageSource, alignment: 'center' });
            } else if (content.logoText) {
                sections.push({ type: 'text', text: `${content.logoText}\n\n`, alignment: 'center', width: 1, height: 1 });
            }
            sections.push({ type: 'text', text: '領収書\n', alignment: 'center', width: 2, height: 2, bold: true });
            if (request.isRetry) {
                sections.push({ type: 'text', text: '再試行\n', alignment: 'center', width: 1, height: 1 });
            }
            sections.push({ type: 'text', text: `${issuedDate(request.issued_at)}\n\n`, alignment: 'center', width: 1, height: 1 });
            sections.push({ type: 'text', text: `\n${addresseeLine(request)}\n${addresseeUnderline()}\n\n`, alignment: 'left', width: 1, height: 1 });
            sections.push({ type: 'text', text: '領収金額\n', alignment: 'center', width: 1, height: 1 });
            sections.push({ type: 'text', text: `${content.totalPrintText}\n`, alignment: 'center', width: 2, height: 2, bold: true });
            sections.push({ type: 'text', text: `${content.afterTotal.join('\n')}\n`, alignment: 'left', width: 1, height: 1 });
            return sections;
        };
        const describeReceipt = (request) => `${dateTime(request.issued_at, true)} / ${formatYen(request.total_amount)}`;
        const receiptKey = (request) => compact(request.checkoutId, 'unknown');
        return { buildReceiptParts, buildReceiptText, buildReceiptPrintPlan, describeReceipt, formatYen, receiptKey };
    };

    window.ProsperSiiReceiptLayout = { create };
})();
