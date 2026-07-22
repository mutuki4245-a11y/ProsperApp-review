import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../wwwroot/js/features/sii-receipt-layout.js', import.meta.url), 'utf8');
const context = { Intl, Math, Number, String, Array, window: {} };
vm.runInNewContext(source, context, { filename: 'sii-receipt-layout.js' });

const layout = context.window.ProsperSiiReceiptLayout.create({ lineWidth: 48 });
const text = layout.buildReceiptText({
    addressee: '田中',
    issued_at: '2026-07-22T12:00:00+09:00',
    total_amount: 4080,
    consumption_tax_amount: 371,
    particulars: 'ご飲食代として',
    issuer: { store_name: 'テスト店舗', address: '東京都', phone: '000', invoice_registration_number: 'T000' },
    payments: [{ method_name: '現金', amount: 3000 }, { method_name: 'クレジット', amount: 1080 }]
});
const lines = text.split('\n');
const addresseeIndex = lines.indexOf('宛名');

assert.ok(addresseeIndex >= 0, '宛名見出しを出力すること');
assert.equal(lines[addresseeIndex + 1], '', '宛名欄の1行目を空けること');
assert.equal(lines[addresseeIndex + 2], '', '宛名欄の2行目を空けること');
assert.match(lines[addresseeIndex + 3], /田中.*-+様$/, '宛名欄の3行目に下線と様を出力すること');

const paymentTitleIndex = lines.indexOf('支払い方法：');
assert.ok(paymentTitleIndex >= 0, '支払い方法見出しを出力すること');
['（現金 3,000円）', '（クレジット 1,080円）'].forEach((payment, index) => {
    const line = lines[paymentTitleIndex + index + 1];
    assert.equal(line.trim(), payment, '支払い方法の内容を保持すること');
    assert.equal(line.endsWith(payment), true, '支払い方法を右寄せすること');
});

console.log('Receipt layout checks passed.');
