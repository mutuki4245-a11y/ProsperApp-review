(() => {
    const compact = (value, fallback = '') => String(value ?? fallback).trim();
    const toAmount = (value) => Math.round(Number(value) || 0);

    const createTextParts = (request, lineWidth) => {
        const width = Number.isFinite(Number(lineWidth)) ? Math.max(24, Number(lineWidth)) : 48;
        const separator = '-'.repeat(width);
        const yen = (value) => `${toAmount(value).toLocaleString('ja-JP')}円`;
        const charWidth = (char) => (char.codePointAt(0) ?? 0) > 0x00ff ? 2 : 1;
        const textWidth = (text) => Array.from(String(text)).reduce((total, char) => total + charWidth(char), 0);
        const spaces = (count) => ' '.repeat(Math.max(0, count));
        const dateTime = (value) => new Intl.DateTimeFormat('ja-JP', {
            year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit'
        }).format(new Date(value));
        const twoColumn = (left, right) => {
            return `${left}${spaces(Math.max(1, width - textWidth(left) - textWidth(right)))}${right}`;
        };
        const lines = [
            compact(request.store_name, '店舗'),
            '会計伝票',
            separator,
            twoColumn('卓番', compact(request.table_display_name, '未設定')),
            twoColumn('入店', dateTime(request.opened_at)),
            twoColumn('退店', dateTime(request.closed_at)),
            twoColumn('客数', `${toAmount(request.customer_count)}人`),
            separator
        ];

        const orders = new Map();
        (Array.isArray(request.orders) ? request.orders : []).forEach((line) => {
            const name = compact(line.name, '商品');
            const unitPrice = toAmount(line.unit_price);
            const key = `${name}\u0000${unitPrice}`;
            const current = orders.get(key) ?? { name, unitPrice, quantity: 0, amount: 0 };
            current.quantity += toAmount(line.quantity);
            current.amount += toAmount(line.amount);
            orders.set(key, current);
        });
        orders.forEach((line) => {
            lines.push(line.name);
            lines.push(twoColumn(`  ${line.unitPrice.toLocaleString('ja-JP')} x ${line.quantity}`, yen(line.amount)));
        });
        (Array.isArray(request.adjustments) ? request.adjustments : []).forEach((line) => {
            lines.push(twoColumn(compact(line.name, '調整'), yen(line.amount)));
        });
        lines.push(separator);
        lines.push(twoColumn('小計', yen(request.subtotal_amount)));
        lines.push(twoColumn('サービス料', yen(request.service_charge_amount)));
        return {
            beforeTotal: `${lines.join('\n')}\n`,
            total: `${twoColumn('合計', yen(request.total_amount))}\n`,
            afterTotal: `${twoColumn('', `（内消費税額 ${yen(request.consumption_tax_amount)}）`)}\n\n\n`
        };
    };

    const getManager = () => {
        if (typeof window.PrinterManager === 'function') return window.PrinterManager;
        try {
            return typeof PrinterManager === 'function' ? PrinterManager : null;
        } catch {
            return null;
        }
    };

    const check = (label, result) => {
        if (!result || Number(result.errorCode) !== 0) {
            throw new Error(`${label}に失敗しました。${result?.errorString || ''}`.trim());
        }
    };

    const call = async (label, action, optional = false) => {
        try {
            const result = await action();
            check(label, result);
            return result;
        } catch (error) {
            if (optional) return null;
            throw error;
        }
    };

    const appendText = (manager, text) => call('印字データ送信', () => manager.appendText({ text }));

    const printNow = async (request) => {
        const Manager = getManager();
        if (!Manager) throw new Error('SII Web SDK Serverを利用できません。');
        const config = window.prosperSiiReceiptPrinter ?? {};
        const manager = new Manager({ host: compact(config.host, 'localhost') });
        let started = false;
        try {
            await call('SII Web SDK Server接続', () => manager.start({}));
            started = true;
            if (compact(config.codePage)) await call('コードページ設定', () => manager.setCodePage({ codePage: config.codePage }), true);
            if (compact(config.internationalCharacter)) await call('国際文字設定', () => manager.setInternationalCharacter({ internationalCharacter: config.internationalCharacter }), true);
            const statementRequest = {
                ...request,
                store_name: compact(config.storeName) || request.store_name
            };
            const text = createTextParts(statementRequest, config.lineWidth);
            await appendText(manager, text.beforeTotal);
            await appendText(manager, text.total);
            await appendText(manager, text.afterTotal);
            await call('紙送り', () => manager.appendFeed({ value: 2 }), true);
            await call('カット指定', () => manager.appendCut({ cuttingMethod: 'partial' }), true);
            await call('印刷実行', () => manager.doPrint({}));
        } finally {
            if (started) await call('SII Web SDK Server切断', () => manager.stop({}), true);
        }
    };

    const print = (request) => {
        const job = () => printNow(request);
        return typeof window.ProsperSiiPrintQueue?.enqueue === 'function'
            ? window.ProsperSiiPrintQueue.enqueue(job)
            : job();
    };

    window.ProsperCheckoutStatementPrinter = { print };
})();
