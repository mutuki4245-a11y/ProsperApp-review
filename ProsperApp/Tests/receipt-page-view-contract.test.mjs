import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [markup, pageModel, inputModel, source, sql, receiptSql] = await Promise.all([
    readFile(new URL('../Pages/Closing/Receipts.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Closing/Receipts.cshtml.cs', import.meta.url), 'utf8'),
    readFile(new URL('../Features/Receipts/QuickEntryInputModel.cs', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/receipt-work-queue.js', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/21_receipt_work_queue.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/06_receipts.sql', import.meta.url), 'utf8')
]);
const closingCss = await readFile(new URL('../wwwroot/css/features/closing.css', import.meta.url), 'utf8');

assert.match(pageModel, /public IActionResult OnGet\(\)/, '領収書Razor GETは同期処理を待たないshellにすること');
assert.doesNotMatch(pageModel, /OnGetAsync/, '領収書Razor GETからDB readを行わないこと');
assert.match(pageModel, /OnGetWorkQueueAsync\(string\? resumeCursor/, 'resume cursor付きwork queue readを持つこと');
assert.match(pageModel, /OnPostQueueAdvanceAsync/, '保存と除外は単一JSON mutationへ送ること');
assert.doesNotMatch(pageModel, /SaveQuickEntry|MarkScanMistake|RedirectToPage/, '旧handlerとredirectを残さないこと');
assert.match(inputModel, /取引日/, '領収書入力日は支払日ではなく取引日として表示すること');

assert.match(markup, /data-receipt-work-queue-form/, '状態非依存の入力shellを描画すること');
assert.match(markup, /data-receipt-failed-section/, '確定失敗commandの再編集領域を持つこと');
assert.match(markup, /tablet-entry__workspace/, '領収書入力はビューアーと入力フォームの2カラムshellを使うこと');
assert.match(markup, /preview-toolbar/, '左ビューアーのファイル操作toolbarを持つこと');
assert.match(markup, /data-account-value="@value" aria-pressed="false"/, '科目ボタンはaria-pressedで選択状態を表せること');
assert.match(closingCss, /\.tablet-entry__workspace\s*\{[\s\S]*grid-template-columns: minmax\(520px, 1\.1fr\) minmax\(360px, 0\.9fr\)/, '領収書入力は左ビューアー・右フォームの2カラムにすること');
assert.match(closingCss, /\.account-sheet--modal\s*\{[\s\S]*grid-template-columns: repeat\(3, minmax\(0, 1fr\)\)/, '科目モーダルは見通しのよい均一グリッドにすること');
assert.match(source, /indexedDB\.open/, 'outboxをIndexedDBへ保存すること');
assert.match(source, /state\.pending\[0\]/, 'outboxの先頭commandだけを送ること');
assert.match(source, /ProsperSaveResponse\?\.isRejectedStatus/, '確定失敗だけを失敗一覧へ移すこと');
assert.match(source, /operationId: createOperationId\(\)/, '各commandへoperation IDを付けること');
assert.match(source, /editingFailedIndex/, '失敗commandを再編集して新規commandへ投入できること');
assert.match(source, /resumeCursor/, 'bufferを使い切った場合だけresume cursorで補充すること');

assert.match(sql, /create or replace function store\.get_current_receipt_work_queue/, 'work queue read RPCを定義すること');
assert.match(sql, /create or replace function store\.advance_receipt_work_queue_v2/, '唯一の永続進行mutationを定義すること');
assert.match(sql, /interval '30 days'/, 'operation結果を30日保持すること');
assert.match(sql, /request_hash/, '同じoperation IDへ異なる本文を再利用できないこと');
assert.match(receiptSql, /store_business_day_receipt_expenses/, '領収書支出は入力した営業日へ紐づけること');
assert.match(sql, /receipt_reimbursement_business_day_required/, '保存時に現在営業日を必須にすること');
assert.match(sql, /'journal_date', p_payment_date/, '仕訳日は画面入力の取引日を使うこと');

console.log('Receipt work queue contract checks passed.');
