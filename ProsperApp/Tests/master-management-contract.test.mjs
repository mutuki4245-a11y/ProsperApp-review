import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');
const [sql, sync, indexModel, browser, editor, tables, staffs, items] = await Promise.all([
    read('Sql/store_rpc/16_management_master_snapshot.sql'),
    read('Infrastructure/Supabase/SupabaseManagementMasterSynchronization.cs'),
    read('Pages/Management/Index.cshtml.cs'),
    read('wwwroot/js/features/management-master.js'),
    read('wwwroot/js/features/management-editor.js'),
    read('Pages/Management/Tables.cshtml.cs'),
    read('Pages/Management/Staffs.cshtml.cs'),
    read('Pages/Management/Items.cshtml.cs')
]);

assert.match(sql, /create or replace function store\.get_management_master_snapshot/i);
assert.match(sql, /create or replace function store\.save_management_master_v2/i);
assert.match(sql, /management_master_operation_results/i);
for (const area of ['tables', 'casts', 'staffs', 'items', 'nominations', 'pricing']) {
    assert.match(sql, new RegExp(`'${area}'`));
}
assert.match(sync, /store\.get_management_master_snapshot/);
assert.match(sync, /store\.save_management_master_v2/);
assert.match(indexModel, /OnGetMasterSnapshotAsync/);
assert.match(indexModel, /OnPostMasterMutationAsync/);
assert.match(browser, /ProsperSync\.getStore/);
assert.match(browser, /knownRevision/);
assert.match(browser, /sessionStorage\.setItem\(pendingMutationKey/);
assert.match(browser, /operationId: crypto\.randomUUID\(\)/);
assert.match(browser, /store\.replace\(nextPayload, response\.revision/);
assert.match(editor, /managementMaster\.mutate\(config\.area, action, payload\)/);
for (const shell of [tables, staffs, items]) {
    assert.match(shell, /IActionResult OnGet\(\)/);
    assert.doesNotMatch(shell, /OnPost|RedirectToPage/);
}

console.log('Management master v2 contract checks passed.');
