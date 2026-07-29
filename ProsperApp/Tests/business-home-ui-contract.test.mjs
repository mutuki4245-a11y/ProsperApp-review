import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [layoutMarkup, indexMarkup, businessHomeSource, editorSource, mastersRpc, castSalesRpc, snapshotRpc] = await Promise.all([
    readFile(new URL('../Pages/Shared/_Layout.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-home.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-slip-editor.js', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/02_store_masters.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/07_cast_sales_adjustments.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/09_business_home_snapshot.sql', import.meta.url), 'utf8')
]);

assert.match(layoutMarkup, /asp-page="\/Closing\/Index">締め作業/, '締め作業は最上部ナビに置くこと');
assert.equal(layoutMarkup.includes('app-workflow-nav'), false, '営業中/締め作業の2段目業務フロータブは置かないこと');

assert.match(indexMarkup, /data-business-slip-detail-modal/, '営業中詳細は専用モーダルを持つこと');
assert.equal(businessHomeSource.includes('dataset.businessSlipOpenDetail'), true, '伝票パネルから詳細モーダルを開くこと');
assert.equal(businessHomeSource.includes('businessSlipDetailsToggle'), false, '詳細をカード内アコーディオンで開閉しないこと');
assert.match(businessHomeSource, /business-slip-detail-karaoke/, 'カラオケ数量変更は詳細モーダル内に置くこと');
assert.equal(editorSource.includes('window.ProsperBusinessSlipEditor'), true, '詳細モーダルから編集モーダルへ安全に遷移できる公開APIを持つこと');

assert.equal(businessHomeSource.includes('`ご新規様${lineNo || \'\'}'), true, '営業中の楽観表示は空欄客名を ご新規様N にすること');
assert.equal(editorSource.includes('`ご新規様${lineNo || \'\'}'), true, '編集モーダルも空欄客名を ご新規様N にすること');

for (const source of [mastersRpc, castSalesRpc, snapshotRpc]) {
    assert.match(source, /'ご新規様' \|\| c\.line_no::text/, 'SQLの空欄客名表示も ご新規様N にすること');
}

console.log('Business home UI contract checks passed.');
