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
const addresseeIndex = lines.findIndex((line) => line.includes('田中') && line.endsWith('様'));

assert.equal(lines.includes('宛名'), false, '宛名ラベルを出力しないこと');
assert.equal(lines[addresseeIndex - 1], '', '宛名欄の前に余白を確保すること');
assert.equal(lines[addresseeIndex - 2], '', '宛名欄の前に余白を確保すること');
assert.match(lines[addresseeIndex], /田中.*様$/, '宛名欄の最下行に氏名と様を出力すること');
assert.equal(lines[addresseeIndex].indexOf('田中'), 22, '入力氏名を宛名欄の中央に配置すること');
assert.equal(lines[addresseeIndex].includes('-'), false, '氏名と様は下線の上に配置すること');
assert.match(lines[addresseeIndex + 1], /^  -+$/, '宛名欄の直下に下線を出力すること');

const paymentTitleIndex = lines.indexOf('支払い方法：');
assert.ok(paymentTitleIndex >= 0, '支払い方法見出しを出力すること');
['（現金 3,000円）', '（クレジット 1,080円）'].forEach((payment, index) => {
    const line = lines[paymentTitleIndex + index + 1];
    assert.equal(line.trim(), payment, '支払い方法の内容を保持すること');
    assert.equal(line.endsWith(payment), true, '支払い方法を右寄せすること');
});

const stampText = layout.buildReceiptText({
    total_amount: 55000,
    issued_at: '2026-07-22T12:00:00+09:00',
    issuer: {}
});
const stampLines = stampText.split('\n');
const stampTitleIndex = stampLines.indexOf('収入印紙欄');
const stampEdge = '+----------------+';
const stampInside = '|                |';

assert.ok(stampTitleIndex >= 0, '55,000円以上では収入印紙欄を出力すること');
assert.equal(stampLines[stampTitleIndex + 1], stampEdge, '収入印紙欄の上辺を出力すること');
for (let offset = 2; offset <= 6; offset += 1) {
    assert.equal(stampLines[stampTitleIndex + offset], stampInside, '収入印紙欄を正方形に近い高さまで確保すること');
}
assert.equal(stampLines[stampTitleIndex + 7], stampEdge, '収入印紙欄の下辺を出力すること');

const cappedTimeReceipt = layout.buildReceiptText({
    issued_at: '2026-07-23T02:10:00+09:00',
    total_amount: 1000,
    issuer: {}
});
assert.match(cappedTimeReceipt, /発行日\s+2026\/07\/23 01:00/, '領収書の発行時刻は実日付の01:00を上限に印字すること');
assert.equal(
    layout.describeReceipt({ issued_at: '2026-07-23T02:10:00+09:00', total_amount: 1000 }),
    '2026/07/23 01:00 / 1,000円',
    '再印刷用の説明も実日付の同じ上限時刻で表示すること'
);

console.log('Receipt layout checks passed.');
