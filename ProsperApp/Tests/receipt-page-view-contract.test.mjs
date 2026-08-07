import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [markup, pageModel, source, sql] = await Promise.all([
    readFile(new URL('../Pages/Closing/Receipts.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Closing/Receipts.cshtml.cs', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/receipt-work-queue.js', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/21_receipt_work_queue.sql', import.meta.url), 'utf8')
]);

assert.match(pageModel, /public IActionResult OnGet\(\)/, '領収書Razor GETは同期処理を待たないshellにすること');
assert.doesNotMatch(pageModel, /OnGetAsync/, '領収書Razor GETからDB readを行わないこと');
assert.match(pageModel, /OnGetWorkQueueAsync\(string\? resumeCursor/, 'resume cursor付きwork queue readを持つこと');
assert.match(pageModel, /OnPostQueueAdvanceAsync/, '保存と除外は単一JSON mutationへ送ること');
assert.doesNotMatch(pageModel, /SaveQuickEntry|MarkScanMistake|RedirectToPage/, '旧handlerとredirectを残さないこと');

assert.match(markup, /data-receipt-work-queue-form/, '状態非依存の入力shellを描画すること');
assert.match(markup, /data-receipt-failed-section/, '確定失敗commandの再編集領域を持つこと');
assert.match(source, /indexedDB\.open/, 'outboxをIndexedDBへ保存すること');
assert.match(source, /state\.pending\[0\]/, 'outboxの先頭commandだけを送ること');
assert.match(source, /validation_error'[\s\S]*stale_work_item/, '確定失敗を失敗一覧へ移すこと');
assert.match(source, /operationId: createOperationId\(\)/, '各commandへoperation IDを付けること');
assert.match(source, /editingFailedIndex/, '失敗commandを再編集して新規commandへ投入できること');
assert.match(source, /resumeCursor/, 'bufferを使い切った場合だけresume cursorで補充すること');

assert.match(sql, /create or replace function store\.get_current_receipt_work_queue/, 'work queue read RPCを定義すること');
assert.match(sql, /create or replace function store\.advance_receipt_work_queue_v2/, '唯一の永続進行mutationを定義すること');
assert.match(sql, /interval '30 days'/, 'operation結果を30日保持すること');
assert.match(sql, /request_hash/, '同じoperation IDへ異なる本文を再利用できないこと');

console.log('Receipt work queue contract checks passed.');
