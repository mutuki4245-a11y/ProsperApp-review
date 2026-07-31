import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

const masterSql = read('Sql/store_rpc/02_store_masters.sql');
const schemaSql = read('Sql/store_order_accounting_tables.sql');
const edge = read('supabase/functions/prosper-rpc/index.ts');
const tableRepository = read('Infrastructure/Supabase/SupabaseStoreTableAdminRepository.cs');
const itemRepository = read('Infrastructure/Supabase/SupabaseStoreItemAdminRepository.cs');
const tablePage = read('Pages/Management/Tables.cshtml');
const tablePageModel = read('Pages/Management/Tables.cshtml.cs');
const itemPage = read('Pages/Management/Items.cshtml');
const itemPageModel = read('Pages/Management/Items.cshtml.cs');
const settingsPage = read('Pages/Settings/Index.cshtml');
const settingsPageModel = read('Pages/Settings/Index.cshtml.cs');
const adminModeService = read('Services/AdminModeService.cs');

const functionBlock = (source, name) => {
    const escapedName = name.replace('.', '\\.');
    const match = new RegExp(
        `create\\s+or\\s+replace\\s+function\\s+${escapedName}\\b[\\s\\S]*?\\$\\$;`,
        'i'
    ).exec(source);
    assert.ok(match, `${name} の定義が必要です。`);
    return match[0];
};

for (const rpc of [
    'store.get_table_admin_list',
    'store.upsert_table',
    'store.delete_table',
    'store.delete_item_category'
]) {
    assert.match(edge, new RegExp(`"${rpc.replace('.', '\\.')}"`), `${rpc} をEdge allowlistへ追加してください。`);
    assert.ok(functionBlock(masterSql, rpc));
}

const tableList = functionBlock(masterSql, 'store.get_table_admin_list');
assert.match(tableList, /t\.sort_order/i);
assert.match(tableList, /t\.is_active/i);
assert.doesNotMatch(tableList, /t\.is_active\s*=\s*true/i);

const deleteTable = functionBlock(masterSql, 'store.delete_table');
assert.match(deleteTable, /delete from public\.store_table_master/i);
assert.match(deleteTable, /t\.department_id = p_department_id/i);

const deleteItem = functionBlock(masterSql, 'store.delete_item');
assert.match(deleteItem, /delete from public\.store_item_master/i);
assert.match(deleteItem, /i\.item_type <> 'standard'/i);

const deleteCategory = functionBlock(masterSql, 'store.delete_item_category');
assert.match(deleteCategory, /store_item_category_in_use/i);
assert.match(deleteCategory, /delete from public\.store_item_category_master/i);

assert.match(schemaSql, /table_code_snapshot text/i);
assert.match(schemaSql, /drop constraint if exists store_slips_table_id_fkey/i);
assert.match(schemaSql, /drop constraint if exists store_order_lines_item_id_fkey/i);

assert.match(tableRepository, /store\.get_table_admin_list/);
assert.match(tableRepository, /store\.upsert_table/);
assert.match(tableRepository, /store\.delete_table/);
assert.match(tableRepository, /StoreMasterCacheKeys\.ClearTables/);
assert.match(itemRepository, /store\.delete_item_category/);

assert.match(settingsPage, /asp-for="Input\.AdminMode"/);
assert.match(settingsPageModel, /_adminModeService\.SetEnabled\(Input\.AdminMode\)/);
assert.match(adminModeService, /Session\.GetString\(SessionKey\)/);
assert.match(adminModeService, /session\.SetString\(SessionKey,\s*"1"\)/);

for (const pageModel of [tablePageModel, itemPageModel]) {
    assert.match(pageModel, /IAdminModeService/);
    assert.match(pageModel, /変更するには管理者設定で管理者モードを有効にしてください/);
}

assert.match(tablePage, /asp-page-handler="Save"/);
assert.match(tablePage, /formaction="\?handler=Delete"/);
assert.match(itemPage, /formaction="\?handler=DeleteCategory"/);
assert.doesNotMatch(
    tablePage,
    /onclick="[^"]*@table\./,
    '卓番の保存値をinline JavaScriptへ補間しないでください。'
);
assert.doesNotMatch(
    itemPage,
    /onclick="[^"]*@category\./,
    'カテゴリの保存値をinline JavaScriptへ補間しないでください。'
);
