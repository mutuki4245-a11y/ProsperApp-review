import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../wwwroot/js/features/sii-receipt-layout.js', import.meta.url), 'utf8');
const context = { Intl, Math, Number, String, Array, window: {} };
vm.runInNewContext(source, context, { filename: 'sii-receipt-layout.js' });

const layout = context.window.ProsperSiiReceiptLayout.create({ lineWidth: 48 });
const request = {
    addressee: '田中',
    issued_at: '2026-07-22T12:00:00+09:00',
    total_amount: 4080,
    taxable_amount_including_tax: 4080,
    consumption_tax_amount: 371,
    particulars: 'ご飲食代として',
    issuer: {
        company_name: '株式会社テスト',
        store_name: 'テスト店舗',
        address: '東京都',
        phone: '000',
        invoice_registration_number: 'T000',
        logo: '/images/logo.png'
    },
    payments: [{ method_name: '現金', amount: 3000 }, { method_name: 'クレジット', amount: 1080 }]
};
const text = layout.buildReceiptText(request);
const lines = text.split('\n');
const addresseeIndex = lines.findIndex((line) => line.includes('田中') && line.endsWith('様'));

assert.equal(lines[0].trim(), '領収書', '領収書見出しを最上部のテキストにすること');
assert.equal(lines[1].trim(), '2026年 7月 22日（水）', '発行日は和暦ではなく実日付を印字すること');
assert.equal(lines.some((line) => line.includes('/images/logo.png')), false, '画像ロゴURLを文字として印字しないこと');
assert.equal(lines.includes('宛名'), false, '宛名ラベルを出力しないこと');
assert.equal(lines[addresseeIndex - 1], '', '宛名欄の前に余白を確保すること');
assert.equal(lines[addresseeIndex - 2], '', '宛名欄の前に余白を確保すること');
assert.match(lines[addresseeIndex], /田中.*様$/, '宛名欄の最下行に氏名と様を出力すること');
assert.equal(lines[addresseeIndex].indexOf('田中'), 22, '入力氏名を宛名欄の中央に配置すること');
assert.equal(lines[addresseeIndex].includes('-'), false, '氏名と様は下線の上に配置すること');
assert.match(lines[addresseeIndex + 1], /^  -+$/, '宛名欄の直下に下線を出力すること');

const amountLabelIndex = lines.findIndex((line) => line.trim() === '領収金額');
assert.ok(amountLabelIndex > addresseeIndex, '宛名欄の後に領収金額見出しを出力すること');
assert.equal(lines[amountLabelIndex + 1].trim(), '¥ 4,080 -', '領収金額を参考書式の金額表記で出力すること');
assert.ok(lines.some((line) => line.trim() === 'ご飲食代として' && line.startsWith(' ')), '但し書きを中央寄せで出力すること');
assert.ok(lines.some((line) => line.trim() === '上記 正に領収致しました。' && line.startsWith(' ')), '領収文言を出力すること');
assert.ok(lines.some((line) => /^10%対象\s+4,080円$/.test(line)), '10%対象額を出力すること');
assert.ok(lines.some((line) => /^内消費税額\s+371円$/.test(line)), '内消費税額を出力すること');

const paymentTitleIndex = lines.indexOf('支払い方法');
assert.ok(paymentTitleIndex >= 0, '支払い方法見出しを出力すること');
['現金 3,000円', 'クレジット 1,080円'].forEach((payment, index) => {
    const line = lines[paymentTitleIndex + index + 1];
    assert.equal(line.trim(), payment, '支払い方法の内容を保持すること');
    assert.equal(line.endsWith(payment), true, '支払い方法を右寄せすること');
});

const issuerIndex = lines.indexOf('株式会社テスト');
assert.ok(issuerIndex > paymentTitleIndex, '発行者情報は紙面下部に出力すること');
assert.deepEqual(
    lines.slice(issuerIndex, issuerIndex + 5),
    ['株式会社テスト', 'テスト店舗', '東京都', 'TEL 000', '登録番号 T000'],
    '会社名、店舗名、住所、電話、登録番号の順で出力すること'
);

const plan = layout.buildReceiptPrintPlan(request);
assert.equal(plan[0].type, 'image', '画像ロゴは印刷プランの先頭に置くこと');
assert.equal(plan[0].source, '/images/logo.png', '画像ロゴは発行者スナップショットのURLを使うこと');
assert.equal(plan[0].alignment, 'center', '画像ロゴは中央印字すること');
assert.equal(plan[1].text, '領収書\n', '領収書見出しを印字すること');
assert.equal(plan[1].alignment, 'center', '領収書見出しは中央印字すること');
assert.equal(plan[1].width, 2, '領収書見出しは横2倍で印字すること');
assert.equal(plan[1].height, 2, '領収書見出しは縦2倍で印字すること');
assert.equal(plan[1].bold, true, '領収書見出しは強調すること');
assert.ok(plan.some((section) => section.text === '¥ 4,080 -\n' && section.width === 2 && section.height === 2 && section.bold), '合計額は2倍文字で印字すること');

const textLogoPlan = layout.buildReceiptPrintPlan({ total_amount: 1000, issued_at: '2026-07-22T12:00:00+09:00', issuer: { logo: '【テストロゴ】' } });
assert.equal(textLogoPlan[0].type, 'text', '旧文字ロゴは画像として扱わないこと');
assert.match(textLogoPlan[0].text, /【テストロゴ】/, '旧文字ロゴは文字として印字すること');

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
assert.match(cappedTimeReceipt, /2026年 7月 23日（木）/, '領収書の紙面には会計確定の実日付を印字すること');
assert.equal(
    layout.describeReceipt({ issued_at: '2026-07-23T02:10:00+09:00', total_amount: 1000 }),
    '2026/07/23 01:00 / 1,000円',
    '再印刷用の説明は従来どおり実日付の同じ上限時刻で表示すること'
);

console.log('Receipt layout checks passed.');
