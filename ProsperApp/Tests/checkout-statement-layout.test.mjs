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
        { name: '担当(佐藤)', item_type: 'nomination_fee', source_type: 'nomination_fee', quantity: 1, unit_price: 2000, amount: 2000 },
        { name: '同伴(田中)', item_type: 'nomination_fee', source_type: 'nomination_fee', quantity: 1, unit_price: 3000, amount: 3000 },
        { name: 'ドリンク', back_cast_display_name: '田中', quantity: 2, unit_price: 1000, amount: 2000 },
        { name: 'ドリンク', back_cast_display_name: '佐藤', quantity: 1, unit_price: 1000, amount: 1000 },
        { name: 'セット料金', item_type: 'set_fee', source_type: 'automatic_pricing', quantity: 2, unit_price: 3000, amount: 6000 },
        { name: '延長料金', item_type: 'extension_fee', source_type: 'automatic_pricing', quantity: 1, unit_price: 2000, amount: 2000 },
        { name: 'カラオケ', item_type: 'karaoke', quantity: 1, unit_price: 200, amount: 200 }
    ],
    pricing_lines: [],
    adjustments: [],
    order_subtotal_amount: 17000,
    pricing_subtotal_amount: 0,
    subtotal_amount: 17000,
    service_charge_amount: 3400,
    consumption_tax_amount: 1855,
    total_amount: 20400
}, 48).beforeTotal.split('\n');

assert.match(statement.find((line) => line.startsWith('退店')) || '', /2026\/07\/22 25:00$/, '退店時刻は25:00を上限に印字すること');
const companionIndex = statement.indexOf('同伴(田中)');
const nominationIndex = statement.indexOf('担当(佐藤)');
const setFeeIndex = statement.indexOf('セット料金');
const extensionFeeIndex = statement.indexOf('延長料金');
const karaokeIndex = statement.indexOf('カラオケ');
const orderIndex = statement.indexOf('ドリンク/田中');
assert.ok(companionIndex >= 0 && nominationIndex >= 0, '指名料金はキャスト名付きの明細名で印字すること');
assert.ok(setFeeIndex < extensionFeeIndex && extensionFeeIndex < companionIndex && companionIndex < karaokeIndex && karaokeIndex < orderIndex, '会計伝票はセット料金、延長料金、指名料金、カラオケ、その他の順に印字すること');
assert.ok(nominationIndex < karaokeIndex, '同伴以外の指名料金もカラオケより先に印字すること');
assert.ok(statement.includes('ドリンク/田中'), 'バック対象キャストを商品名に併記すること');
assert.ok(statement.includes('ドリンク/佐藤'), '別のバック対象キャストは別明細にすること');
assert.equal(statement.filter((line) => line === 'ドリンク/田中').length, 1, '同じバック対象キャストの明細だけを集約すること');
assert.ok(statement.some((line) => /1,000 x 3/.test(line)), '同じバック対象キャストの数量を合算すること');
assert.ok(!statement.includes('時間料金'), '自動料金を別枠にせずシステム商品として印字すること');
assert.ok(statement.includes('セット料金') && statement.includes('延長料金'), 'セット料金と延長料金をシステム商品として印字すること');

const rpcSource = await readFile(new URL('../Sql/store_rpc/08_checkout_ready.sql', import.meta.url), 'utf8');
assert.match(rpcSource, /when ol\.source_type = 'nomination_fee' and sc\.nomination_type = 'companion'/, '同伴指名の名称をRPCで決めること');
assert.match(rpcSource, /format\('同伴\(%s\)'/, '同伴指名の会計伝票名を含めること');
assert.match(rpcSource, /format\('担当\(%s\)'/, 'その他指名の会計伝票名を含めること');
assert.match(rpcSource, /'back_cast_display_name', coalesce\(back_cast\.display_name, ''\)/, '明細のバック対象を印字データへ含めること');
assert.match(rpcSource, /'business_date', v_slip\.business_date/, '会計伝票の退店時刻丸め用に営業日を返すこと');
assert.match(rpcSource, /from public\.store_slip_pricing_lines pl/, '固定済み自動料金を会計伝票へ含めること');
assert.match(rpcSource, /store\.calculate_slip_pricing\(p_department_id, p_slip_id, p_closed_at\)/, '会計伝票出力時に選択退店時刻で自動料金を固定すること');
assert.match(rpcSource, /from store\.ensure_pricing_system_items\(p_department_id\)/, '固定済み料金ごとにシステム商品を用意すること');
assert.match(rpcSource, /'automatic_pricing'/, '自動料金の注文行を手動注文と区別すること');
assert.match(rpcSource, /'item_type', coalesce\(\s*i\.item_type/, '会計伝票RPCは商品種別を印字データへ含めること');
assert.match(rpcSource, /when 'set_fee' then 0[\s\S]*when 'extension_fee' then 1[\s\S]*when 'nomination_fee' then 2[\s\S]*when 'karaoke' then 3/, '会計伝票RPCは明細種別の順序を固定すること');

console.log('Checkout statement layout checks passed.');
