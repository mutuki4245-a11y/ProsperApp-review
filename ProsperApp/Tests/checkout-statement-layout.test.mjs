import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const printerSource = await readFile(new URL('../wwwroot/js/features/sii-checkout-statement-printer.js', import.meta.url), 'utf8');
const context = { Intl, Math, Number, String, Array, Map, Date, Object, window: {} };
vm.runInNewContext(printerSource, context, { filename: 'sii-checkout-statement-printer.js' });

const statement = context.window.ProsperCheckoutStatementPrinter.createTextParts({
    store_name: 'テスト店舗',
    table_display_name: 'A1',
    business_date: '2026-07-22',
    opened_at: '2026-07-22T19:00:00+09:00',
    closed_at: '2026-07-23T02:10:00+09:00',
    customer_count: 2,
    orders: [
        { name: 'ドリンク', back_cast_display_name: '田中', quantity: 1, unit_price: 1000, amount: 1000 },
        { name: '担当(佐藤)', source_type: 'nomination_fee', quantity: 1, unit_price: 2000, amount: 2000 },
        { name: '同伴(田中)', source_type: 'nomination_fee', quantity: 1, unit_price: 3000, amount: 3000 },
        { name: 'ドリンク', back_cast_display_name: '田中', quantity: 2, unit_price: 1000, amount: 2000 },
        { name: 'ドリンク', back_cast_display_name: '佐藤', quantity: 1, unit_price: 1000, amount: 1000 }
    ],
    adjustments: [],
    subtotal_amount: 9000,
    service_charge_amount: 1800,
    consumption_tax_amount: 982,
    total_amount: 10800
}, 48).beforeTotal.split('\n');

assert.match(statement.find((line) => line.startsWith('退店')) || '', /2026\/07\/22 25:00$/, '退店時刻は25:00を上限に印字すること');
const companionIndex = statement.indexOf('同伴(田中)');
const nominationIndex = statement.indexOf('担当(佐藤)');
const orderIndex = statement.indexOf('ドリンク/田中');
assert.ok(companionIndex >= 0 && nominationIndex >= 0, '指名料金はキャスト名付きの明細名で印字すること');
assert.ok(companionIndex < orderIndex && nominationIndex < orderIndex, '指名料金を注文明細より先に印字すること');
assert.ok(statement.includes('ドリンク/田中'), 'バック対象キャストを商品名に併記すること');
assert.ok(statement.includes('ドリンク/佐藤'), '別のバック対象キャストは別明細にすること');
assert.equal(statement.filter((line) => line === 'ドリンク/田中').length, 1, '同じバック対象キャストの明細だけを集約すること');
assert.ok(statement.some((line) => /1,000 x 3/.test(line)), '同じバック対象キャストの数量を合算すること');

const rpcSource = await readFile(new URL('../Sql/store_rpc/08_checkout_ready.sql', import.meta.url), 'utf8');
assert.match(rpcSource, /when ol\.source_type = 'nomination_fee' and sc\.nomination_type = 'companion'/, '同伴指名の名称をRPCで決めること');
assert.match(rpcSource, /format\('同伴\(%s\)'/, '同伴指名の会計伝票名を含めること');
assert.match(rpcSource, /format\('担当\(%s\)'/, 'その他指名の会計伝票名を含めること');
assert.match(rpcSource, /'back_cast_display_name', coalesce\(back_cast\.display_name, ''\)/, '明細のバック対象を印字データへ含めること');
assert.match(rpcSource, /'business_date', v_business_date/, '領収書の発行時刻丸め用に営業日を返すこと');

console.log('Checkout statement layout checks passed.');
