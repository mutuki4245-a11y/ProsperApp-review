import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [markup, pageModel, source] = await Promise.all([
    readFile(new URL('../Pages/Orders/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Orders/Index.cshtml.cs', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/order-entry.js', import.meta.url), 'utf8')
]);

assert.match(markup, /id="orderSlipCountBadge">取得中/, '注文対象件数は初期0件ではなく取得中で表示すること');
assert.match(markup, /id="refreshSlipOptionsButton"[\s\S]*?>再取得<\/button>/, '注文端末は注文対象伝票を手動再取得できること');
assert.match(markup, /id="orderItemSelectionWarning"[\s\S]*?先に卓番を選択してください/, '卓未選択時の理由を商品パネルに表示すること');
assert.match(markup, /departmentId = Model\.StoreContext\?\.DepartmentId/, '注文端末下書きキーに店舗IDを含めること');
assert.match(markup, /businessDayId = Model\.CurrentBusinessDay\?\.BusinessDayId/, '注文端末下書きキーに営業日IDを含めること');
assert.match(markup, /submitUrl = Url\.Page\("\/Orders\/Index", "SubmitV2"\)/, '注文登録はv2 JSON handlerへ送ること');
assert.match(pageModel, /OnPostSubmitV2Async/, '注文登録のv2 JSON handlerを持つこと');
assert.doesNotMatch(pageModel, /RedirectToPage/, '注文成功後にredirect GETを使わないこと');

assert.match(source, /prosper:order-entry-draft:v1:\$\{pageData\.departmentId\}:\$\{pageData\.businessDayId\}/, '注文端末下書きは店舗・営業日単位のlocalStorageキーを使うこと');
assert.match(source, /const restoreStoredDraft = \(\) => \{[\s\S]*hydrateQueueLines\(draft\.lines\)/, '注文端末はlocalStorage下書きをキューへ復元すること');
assert.match(source, /const persistDraft = \(\) => \{[\s\S]*localStorage\.setItem\(draftStorageKey/, '注文端末はキュー更新をlocalStorageへ保存すること');
assert.match(source, /pendingSubmitCommand/, '結果不明の注文commandを保持すること');
assert.match(source, /pendingSubmitCommand,\s*lines/, '注文commandを下書きと同時に永続化すること');
assert.match(source, /removeUnavailableSlipLines[\s\S]*!hasSlip\(line\.slipId\)[\s\S]*queue\.delete/, '注文端末はopenでなくなった伝票の下書き行を破棄すること');
assert.match(source, /setText\(slipCountBadge, slipOptionsLoaded \? `\$\{slips\.length\} 件` : '取得中'\)/, '注文対象件数はAjax結果で更新すること');
assert.match(source, /itemSelectionWarning\.hidden = hasSelectedSlip/, '卓未選択時だけ警告を表示すること');

console.log('Order entry draft contract checks passed.');
