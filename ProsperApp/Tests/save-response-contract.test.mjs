import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';

const helperSource = await readFile(new URL('../wwwroot/js/save-response.js', import.meta.url), 'utf8');
const context = { window: {} };
vm.runInNewContext(helperSource, context, { filename: 'save-response.js' });
const api = context.window.ProsperSaveResponse;

assert.equal(api.classify({ response: { ok: true, status: 200 }, payload: { status: 'confirmed' } }).confirmed, true);
assert.equal(api.classify({ response: { ok: false, status: 409 }, payload: { status: 'conflict' } }).rejected, true);
assert.equal(api.classify({ response: { ok: false, status: 400 }, payload: { status: 'validation_error' } }).rejected, true);
assert.equal(api.classify({ response: { ok: false, status: 403 }, payload: { status: 'permission_denied' } }).rejected, true);
assert.equal(api.classify({ response: { ok: false, status: 409 }, payload: { status: 'stale_work_item' } }).rejected, true);
assert.equal(api.classify({ response: { ok: false, status: 503 }, payload: { status: 'unavailable' } }).retry, true);
assert.equal(api.classify({ response: { ok: false, status: 400 }, payload: null }).retry, true);
assert.equal(api.classify({ response: { ok: false, status: 200 }, payload: { status: 'unavailable' } }).retry, true);
assert.equal(api.classifyRow({ succeeded: true }).confirmed, true);
assert.equal(api.classifyRow({ succeeded: false, status: 'validation_error' }).rejected, true);
assert.equal(api.classifyRow({ succeeded: false }).retry, true);

const [layout, businessHome, checkout, attendance, receipt] = await Promise.all([
    readFile(new URL('../Pages/Shared/_Layout.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-home.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-checkout.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/attendance-editor.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/receipt-work-queue.js', import.meta.url), 'utf8')
]);

assert.match(layout, /js\/save-response\.js/, '共通保存応答分類ヘルパーを全ページのfeature script前に読み込むこと');
assert.match(businessHome, /classifyRow/, '営業中batch保存は行単位の共通分類を使うこと');
assert.match(checkout, /saveResponse\?\.classify/, '会計mutationは共通分類を使うこと');
assert.match(attendance, /saveResponse\?\.classify/, '勤怠保存は共通分類を使うこと');
assert.match(receipt, /ProsperSaveResponse\?\.isRejectedStatus/, '領収書キューは確定拒否だけ失敗一覧へ移すこと');

console.log('Save response classification contract checks passed.');
