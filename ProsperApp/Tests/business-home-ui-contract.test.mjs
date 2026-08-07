import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [layoutMarkup, indexMarkup, businessHomeSource, slipsCss, editorSource, actionIcons, mastersRpc, castSalesRpc, snapshotRpc, checkoutSource] = await Promise.all([
    readFile(new URL('../Pages/Shared/_Layout.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-home.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/css/features/slips.css', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-slip-editor.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/icons/lucide-actions.svg', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/02_store_masters.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/07_cast_sales_adjustments.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/09_business_home_snapshot.sql', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-checkout.js', import.meta.url), 'utf8')
]);

assert.match(layoutMarkup, /asp-page="\/Closing\/Index">締め作業/, '締め作業は最上部ナビに置くこと');
assert.equal(layoutMarkup.includes('app-workflow-nav'), false, '営業中/締め作業の2段目業務フロータブは置かないこと');
assert.match(
    layoutMarkup,
    /asp-page="\/Index"[\s\S]*?data-loading-lock="false"[\s\S]*?>営業中<\/a>/,
    'shell-firstの営業中トップ遷移では共通の全画面navigation lockを使わないこと'
);

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
assert.match(businessHomeSource, /setText\(elapsed, formatElapsedMinutes\(minutes\)\)/, '在席ラベルを付けずに経過時間だけを表示すること');
assert.equal(businessHomeSource.includes('`在席 ${formatElapsedMinutes(minutes)}`'), false, '経過時間へ在席ラベルを重ねないこと');
assert.match(snapshotRpc, /'closedAt', s\.closed_at/, '会計後の在席時間を退店時刻で固定できること');
assert.match(businessHomeSource, /business-slip-card__person--\$\{kind\}/, 'お客様と指名を一人ずつパネル表示すること');
assert.match(businessHomeSource, /displayName\.includes\('同伴'\) \? '同伴'/, '伝票パネルの同伴系指名区分を同伴へまとめること');
assert.equal(businessHomeSource.includes('CARD_PERSON_PANEL_LIMIT'), false, '一覧の客・指名を固定件数で打ち切らないこと');
assert.match(businessHomeSource, /const compactCardPersonList = \(personList\) =>/, '客・指名の実際の折り返しを一覧上で判定すること');
assert.match(businessHomeSource, /const compactCardPersonGrid = \(grid\) =>/, '横並びの伝票ごとに客・指名枠を調整すること');
assert.match(businessHomeSource, /const maximumCustomerLines = Math\.max\(\.\.\.rowCards\.map\(\(\{ customerLines \}\) => customerLines\)\);/, '横列で必要な最大客行数へパネル高を揃えること');
assert.match(businessHomeSource, /const maximumNominationLines = Math\.max\(\.\.\.rowCards\.map\(\(\{ nominationLines \}\) => nominationLines\)\);/, '横列で必要な最大指名行数へパネル高を揃えること');
assert.match(businessHomeSource, /panel\.getBoundingClientRect\(\)\.top > firstPanelTop \+ 1/, 'チップの実際の折り返し位置から1行・2行を判定すること');
assert.match(businessHomeSource, /personList\.scrollHeight > personList\.clientHeight \+ 1/, '3行目が発生した時だけ超過扱いにすること');
assert.match(businessHomeSource, /new ResizeObserver\(/, 'カード幅が変わった時に折り返しを再計算すること');
assert.match(businessHomeSource, /business-slip-card__person--overflow/, '一覧に入り切らない客・指名を「他」へ畳むこと');
assert.match(businessHomeSource, /buildElement\(\s*'span',[\s\S]*?'他'\s*\)/, '超過分は「他」と表示して詳細モーダルへ任せること');
assert.match(slipsCss, /\.business-slip-grid\s*\{[^}]*align-items:\s*start/s, '横並びカードを内容の最小高さで上揃えにすること');
assert.match(slipsCss, /\.business-slip-grid\s*>\s*\.business-slip-card\.slip-list__row\s*\{[^}]*grid-template-rows:\s*minmax\(70px,\s*auto\) auto 44px/s, 'ヘッダー情報を統合し、操作を下へ固定したカード行構成にすること');
assert.match(slipsCss, /\.business-slip-grid\s+\.business-slip-card__people\s*\{[^}]*--business-slip-customer-size:\s*1\.58rem;[^}]*--business-slip-nomination-size:\s*1\.58rem;[^}]*grid-template-rows:\s*var\(--business-slip-customer-size\) var\(--business-slip-nomination-size\)/s, '横列で全員が1行なら客・指名枠を最小高にすること');
assert.match(slipsCss, /\.business-slip-grid\s+\.business-slip-card__people\[data-business-slip-customer-lines="2"\]\s*\{[^}]*--business-slip-customer-size:\s*3\.44rem/s, '客枠は必要時だけ最大2行にすること');
assert.match(slipsCss, /\.business-slip-grid\s+\.business-slip-card__people\[data-business-slip-nomination-lines="2"\]\s*\{[^}]*--business-slip-nomination-size:\s*3\.44rem/s, '指名枠は必要時だけ最大2行にすること');
assert.match(slipsCss, /@media \(max-width: 575\.98px\)[\s\S]*?\.business-slip-grid\s*>\s*\.business-slip-card\.slip-list__row\s*\{[^}]*grid-template-rows:\s*minmax\(70px,\s*auto\) auto 44px/s, '1列表示では統合ヘッダーの折り返しを許容すること');
assert.match(slipsCss, /\.business-slip-card\.slip-list__row\s*\{[^}]*--business-slip-accent:\s*#4d827c;[^}]*border-radius:\s*6px;[^}]*border-top:\s*4px solid var\(--business-slip-accent\)/s, '状態が読める上端色と静かなカード形状を使うこと');
assert.match(slipsCss, /\.business-slip-card\.slip-list__row\s*\{[^}]*--business-slip-header-background:\s*#dcebea;[^}]*--business-slip-header-hover:\s*#d1e3e1/s, '接客中ヘッダーに判別できる青緑の状態色を使うこと');
assert.match(slipsCss, /\.business-slip-card\.slip-list__row--checkout-ready\s*\{[^}]*--business-slip-header-background:\s*#dbeaf5/s, '会計準備中ヘッダーに判別できる青の状態色を使うこと');
assert.match(slipsCss, /\.business-slip-card\.slip-list__row--checked-out\s*\{[^}]*--business-slip-header-background:\s*#e5e9eb/s, '会計済みヘッダーに判別できるグレーの状態色を使うこと');
assert.match(slipsCss, /\.business-slip-card__header\s*\{[^}]*background:\s*var\(--business-slip-header-background\);[^}]*border-bottom:\s*1px solid var\(--business-slip-header-border\)/s, '伝票ヘッダーへ状態別背景色を適用すること');
assert.match(slipsCss, /\.business-slip-card__status\s*\{[^}]*background:\s*#fff;[^}]*border:\s*1px solid var\(--business-slip-status-border\);[^}]*color:\s*var\(--business-slip-status-color\)/s, '状態表示はヘッダーと調和する輪郭色を使うこと');
assert.equal(businessHomeSource.includes("buildElement('span', null, '入店')"), false, '伝票パネルの入店ラベルを表示しないこと');
assert.equal(businessHomeSource.includes('dataset.businessSlipNumber'), false, '伝票パネルヘッダーに伝票IDを表示しないこと');
assert.match(businessHomeSource, /header\.append\(identity,\s*meta,\s*headerActions\)/, '卓番・入店時刻・人数・在席時間・状態を統合ヘッダーに置くこと');
assert.match(slipsCss, /\.business-slip-card__meta\s*\{[^}]*color:\s*#708088;[^}]*display:\s*flex/s, '統合ヘッダーの時刻・人数・在席時間を控えめに表示すること');
assert.match(slipsCss, /\.business-slip-card__person\s*\{[^}]*background:\s*#f7f9f8;[^}]*border:\s*1px solid #cdd7d8/s, 'お客様チップをニュートラル配色にすること');
assert.match(slipsCss, /\.business-slip-card__person--nomination\s*\{[^}]*background:\s*#fff1f7;[^}]*border-color:\s*#f9a8d4;[^}]*color:\s*#9d174d/s, '指名は赤系配色で区別すること');
assert.match(slipsCss, /\.business-slip-card\.slip-list__row\s*\{[^}]*--business-slip-money-color:\s*#806000/s, '金色を金額専用の変数として持つこと');
assert.match(slipsCss, /\.business-slip-card \.slip-list__amount\s*\{[^}]*color:\s*var\(--business-slip-money-color\)/s, '伝票金額だけに金額色を使うこと');
assert.match(indexMarkup, /data-slip-amount-toggle-container[\s\S]*?type="checkbox"[\s\S]*?role="switch"[\s\S]*?data-slip-amount-toggle/, '会計表示はツールバー内のトグルスイッチにすること');
assert.match(indexMarkup, /data-business-estimated-sales-amount-mask[\s\S]*?data-business-estimated-sales-amount/, '見込み総売上も会計表示トグルで伏せられる構造を持つこと');
assert.equal(indexMarkup.includes('data-slip-amount-reveal'), false, '会計額表示の固定オーバーレイを置かないこと');
assert.match(businessHomeSource, /amountToggle\?\.addEventListener\('change', \(\) => setAmountVisible\(amountToggle\.checked\)\)/, '会計表示トグルの状態を保持すること');
assert.match(businessHomeSource, /syncEstimatedSalesAmountVisibility\(\)/, '会計表示トグル変更時に見込み総売上の表示も同期すること');
assert.match(businessHomeSource, /activity\.append\(\s*hero,\s*customerSummary/s, '伝票サマリーを詳細モーダルの左カラム上側へ置くこと');
assert.match(slipsCss, /\.business-slip-detail-layout\s*\{[^}]*grid-template-columns:\s*minmax\(20rem,\s*0\.85fr\) minmax\(32rem,\s*1\.35fr\)/s, '詳細モーダルは右側の明細幅を広く確保すること');
assert.equal(businessHomeSource.includes('business-slip-detail-karaoke__amount'), false, 'カラオケ行に会計額を重複表示しないこと');
assert.match(editorSource, /const returnsToEditorList = operations\.every[\s\S]*?renderLocalEditor\(state\.section, currentSlip\(state\.slipId\)\)/, '行追加後は一つ前の編集一覧へ戻ること');
assert.equal(businessHomeSource.includes('`${categorySlips.length}伝票`'), false, '卓カテゴリ分割線に伝票数を表示しないこと');
assert.match(businessHomeSource, /buildElement\('div', 'business-slip-paid__body business-slip-grid'\)/, '会計済み伝票も通常伝票と同じカードグリッドを使うこと');
assert.equal(slipsCss.includes('.business-slip-paid__body .business-slip-card {'), false, '会計済み伝票専用の横長カード形状を持たないこと');
assert.match(businessHomeSource, /dataset\.businessReleaseCheckoutReady = ''/, '会計準備中パネルに準備解除操作を持つこと');
assert.match(businessHomeSource, /actionButtons\.append\(customerButton,\s*nominationButton,\s*orderButton,\s*releaseCheckoutButton,\s*checkoutButton,\s*receiptButton,\s*cancelButton\)/, '会計準備中パネルでは準備解除と決済の左右順を逆にすること');
assert.match(checkoutSource, /releaseReady\.dataset\.businessReleaseCheckoutReady[\s\S]*?void release\(slip\)/, 'パネルの準備解除操作から既存解除処理を呼ぶこと');
assert.match(indexMarkup, /id="businessPaymentModal"[\s\S]*?modal-xl[\s\S]*?data-business-payment-detail[\s\S]*?data-business-payment-form/, '決済モーダルを伝票詳細と同じ大型レイアウトにすること');
assert.match(checkoutSource, /buildSlipDetailContent[\s\S]*?includeEditorActions: false[\s\S]*?business-slip-detail-layout__activity[\s\S]*?appendChild\(paymentForm\)/, '決済操作を伝票詳細の左カラムへ追加すること');
assert.equal(businessHomeSource.includes('business-slip-card__summary-label'), false, '伝票パネルにお客様・指名の見出しを表示しないこと');
assert.equal(indexMarkup.includes('business-floor__title'), false, '営業中画面に重複タイトル領域を置かないこと');
assert.equal(indexMarkup.includes('BUSINESS FLOOR'), false, '営業中画面に英字ラベルを表示しないこと');
assert.equal(indexMarkup.includes('伝票単位で、接客・注文・会計をまとめて操作'), false, '営業中画面に操作説明ラベルを表示しないこと');
assert.match(indexMarkup, /data-business-slip-filter="checked_out"/, '会計済みの絞り込みタブを表示すること');
for (const iconId of ['user-round', 'star', 'clipboard-list', 'badge-japanese-yen', 'undo-2']) {
    assert.equal(actionIcons.includes(`id="${iconId}"`), true, `${iconId}アイコンを操作ボタンで利用できること`);
}

assert.equal(businessHomeSource.includes('`ご新規様${lineNo || \'\'}'), true, '営業中の楽観表示は空欄のお客様名を ご新規様N にすること');
assert.equal(editorSource.includes('`ご新規様${lineNo || \'\'}'), true, '編集モーダルも空欄のお客様名を ご新規様N にすること');

for (const source of [mastersRpc, castSalesRpc, snapshotRpc]) {
    assert.match(source, /'ご新規様' \|\| c\.line_no::text/, 'SQLの空欄客名表示も ご新規様N にすること');
}

console.log('Business home UI contract checks passed.');
