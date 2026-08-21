import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [source, page, pageModel, repository] = await Promise.all([
    readFile(new URL('../wwwroot/js/features/business-checkout.js', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Index.cshtml.cs', import.meta.url), 'utf8'),
    readFile(new URL('../Infrastructure/Supabase/SupabaseCheckoutRepository.cs', import.meta.url), 'utf8')
]);

for (const handler of ['IssueCheckoutStatement', 'ReleaseCheckoutReady', 'ConfirmCheckout', 'CancelCheckout']) {
    assert.match(pageModel, new RegExp(`OnPost${handler}Async`));
}
assert.match(page, /issueCheckoutStatementUrl/);
assert.match(page, /releaseCheckoutReadyUrl/);
assert.match(page, /confirmCheckoutUrl/);
assert.match(page, /cancelCheckoutUrl/);
for (const rpc of ['issue_checkout_statement_v2', 'release_checkout_ready_v2', 'confirm_checkout_v2', 'cancel_checkout_v2']) {
    assert.match(repository, new RegExp(`store\\.${rpc}`));
}
assert.match(source, /pendingMutationCommands = new Map\(readPendingMutations\(\)/);
assert.match(source, /sessionStorage\.setItem\(mutationStorageKey/);
assert.match(source, /operationId: crypto\.randomUUID\(\)/);
assert.match(source, /pendingMutationCommands\.get\(commandKey\)/);
assert.match(source, /saveResponse\?\.classify\(\{ response, payload: data \}\)/);
assert.match(source, /if \(classification\.terminal\)[\s\S]*deletePendingMutation\(commandKey\)/);
assert.match(source, /showDialogAlert[\s\S]*AppLoading\?\.hide/, '確認不能モーダルを出す前にロックoverlayを外すこと');
assert.match(source, /sessionStorage\.setItem\(receiptKey\(slipId\)/);
assert.doesNotMatch(source, /ProsperBusinessHome\.reload\s*\(/, '成功mutation直後に全体readを行わないこと');

console.log('Checkout payment v2 UI contract checks passed.');
