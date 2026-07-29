import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [layoutMarkup, indexMarkup, businessHomeSource, editorSource, actionIcons, mastersRpc, castSalesRpc, snapshotRpc] = await Promise.all([
    readFile(new URL('../Pages/Shared/_Layout.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-home.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-slip-editor.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/icons/lucide-actions.svg', import.meta.url), 'utf8'),
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
assert.equal(businessHomeSource.includes('組目'), false, '卓番が重複しても組数ラベルを表示しないこと');
assert.equal(businessHomeSource.includes('data-business-slip-karaoke-summary'), false, '伝票パネルにカラオケ数量を表示しないこと');
assert.equal(businessHomeSource.includes('data-business-slip-elapsed'), true, '伝票パネルに在席時間を表示すること');
assert.match(businessHomeSource, /setInterval\(\(\) => \{[\s\S]*syncElapsedTimes\(\)/, '在席時間を定期更新すること');
assert.match(businessHomeSource, /\['open', 'checkout_ready', 'checked_out'\]/, '接客中・会計準備中・会計済みで在席時間を表示すること');
assert.match(businessHomeSource, /`在席 \$\{formatElapsedMinutes\(minutes\)\}`/, '在席時間をn時間n分で表示すること');
assert.match(snapshotRpc, /'closedAt', s\.closed_at/, '会計後の在席時間を退店時刻で固定できること');
assert.match(businessHomeSource, /business-slip-card__person--\$\{kind\}/, 'お客様と指名を一人ずつパネル表示すること');
assert.equal(businessHomeSource.includes('business-slip-card__summary-label'), false, '伝票パネルにお客様・指名の見出しを表示しないこと');
assert.match(indexMarkup, /data-business-slip-filter="checked_out"/, '会計済みの絞り込みタブを表示すること');
for (const iconId of ['user-round', 'star', 'clipboard-list', 'badge-japanese-yen']) {
    assert.equal(actionIcons.includes(`id="${iconId}"`), true, `${iconId}アイコンを操作ボタンで利用できること`);
}

assert.equal(businessHomeSource.includes('`ご新規様${lineNo || \'\'}'), true, '営業中の楽観表示は空欄のお客様名を ご新規様N にすること');
assert.equal(editorSource.includes('`ご新規様${lineNo || \'\'}'), true, '編集モーダルも空欄のお客様名を ご新規様N にすること');

for (const source of [mastersRpc, castSalesRpc, snapshotRpc]) {
    assert.match(source, /'ご新規様' \|\| c\.line_no::text/, 'SQLの空欄客名表示も ご新規様N にすること');
}

console.log('Business home UI contract checks passed.');
