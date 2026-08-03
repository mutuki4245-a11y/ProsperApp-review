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
const staffRepository = read('Infrastructure/Supabase/SupabaseStoreStaffAdminRepository.cs');
const tablePage = read('Pages/Management/Tables.cshtml');
const tablePageModel = read('Pages/Management/Tables.cshtml.cs');
const itemPage = read('Pages/Management/Items.cshtml');
const itemPageModel = read('Pages/Management/Items.cshtml.cs');
const staffPage = read('Pages/Management/Staffs.cshtml');
const staffPageModel = read('Pages/Management/Staffs.cshtml.cs');
const managementIndexPage = read('Pages/Management/Index.cshtml');
const managementIndexPageModel = read('Pages/Management/Index.cshtml.cs');
const attendancePage = read('Pages/Attendance.cshtml');
const settingsPage = read('Pages/Settings/Index.cshtml');
const settingsPageModel = read('Pages/Settings/Index.cshtml.cs');
const adminModeService = read('Services/AdminModeService.cs');
const storeBootstrapper = read('Infrastructure/Supabase/SupabaseStoreMasterBootstrapper.cs');
const masterCacheKeys = read('Infrastructure/Supabase/StoreMasterCacheKeys.cs');
const businessDayRepository = read('Infrastructure/Supabase/SupabaseBusinessDayRepository.cs');

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
    'store.upsert_table',
    'store.delete_table',
    'store.delete_item_category',
    'store.create_staff',
    'store.delete_staff'
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
assert.match(schemaSql, /create table if not exists public\.store_staff_master/i);
assert.match(schemaSql, /create table if not exists public\.store_staff_attendance/i);
assert.match(schemaSql, /alter table public\.store_staff_master enable row level security/i);
assert.match(schemaSql, /alter table public\.store_staff_attendance enable row level security/i);

assert.match(tableRepository, /IStoreMasterBootstrapper/);
assert.match(tableRepository, /EnsureAsync/);
assert.match(tableRepository, /"table_admin_list"/);
assert.match(tableRepository, /store\.upsert_table/);
assert.match(tableRepository, /store\.delete_table/);
assert.match(tableRepository, /StoreMasterCacheKeys\.ClearTables/);
assert.match(itemRepository, /store\.delete_item_category/);
assert.match(itemRepository, /IStoreMasterBootstrapper/);
assert.match(itemRepository, /"item_admin_catalog"/);
assert.match(staffRepository, /IStoreMasterBootstrapper/);
assert.match(staffRepository, /"staffs"/);
assert.match(staffRepository, /"staffs_admin"/);
assert.match(staffRepository, /store\.create_staff/);
assert.match(staffRepository, /store\.delete_staff/);
assert.match(staffRepository, /StoreMasterCacheKeys\.ClearStaffs/);

assert.match(settingsPage, /asp-for="Input\.AdminMode"/);
assert.match(settingsPageModel, /_adminModeService\.SetEnabled\(Input\.AdminMode\)/);
assert.match(adminModeService, /Session\.GetString\(SessionKey\)/);
assert.match(adminModeService, /session\.SetString\(SessionKey,\s*"1"\)/);

assert.doesNotMatch(tablePageModel, /IAdminModeService/, '卓番操作は通常運用で変更できること');
assert.match(itemPageModel, /IAdminModeService/);
assert.match(itemPageModel, /EnsureCategoryAdminModeAsync/);
assert.match(itemPageModel, /カテゴリを変更するには管理者設定で管理者モードを有効にしてください/);
assert.match(itemPage, /カテゴリの追加・編集・削除には[\s\S]*?管理者モードが必要です。/);
assert.doesNotMatch(staffPageModel, /IAdminModeService/, 'スタッフ操作は通常運用で変更できること');
assert.match(staffPage, /asp-page-handler="Create"/);
assert.match(staffPage, /asp-page-handler="Delete"/);
assert.match(managementIndexPage, /asp-page="\/Management\/Staffs"/);
assert.ok(
    managementIndexPage.indexOf('マスタ情報') < managementIndexPage.indexOf('画面設定') &&
    managementIndexPage.indexOf('画面設定') < managementIndexPage.indexOf('アプリ内キャッシュ'),
    '店舗設定パネルはマスタ情報、画面設定、アプリ内キャッシュの順に表示してください。'
);
assert.match(managementIndexPage, /asp-page-handler="ClearCache"/);
assert.match(managementIndexPage, /更新時まで/);
assert.match(managementIndexPageModel, /OnPostClearCacheAsync/);
assert.match(managementIndexPageModel, /_applicationCache\.ClearAll\(\)/);
assert.doesNotMatch(settingsPage, /アプリ内キャッシュ/);
assert.doesNotMatch(settingsPage, /ClearCache/);
assert.doesNotMatch(settingsPageModel, /IApplicationCache/);
assert.doesNotMatch(settingsPageModel, /OnPostClearCacheAsync/);
assert.match(attendancePage, /id="addAttendanceStaffButton"/);
assert.match(attendancePage, /Input\.SelectedAttendanceKeys/);
assert.match(attendancePage, /person_type/);

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

assert.match(storeBootstrapper, /store\.get_store_bootstrap/);
assert.match(storeBootstrapper, /HydrateCaches\(payload\)/);
assert.match(storeBootstrapper, /StoreMasterCacheKeys\.SetRuntime/);
assert.match(masterCacheKeys, /cache\.Set\(key, value, null, "マスタ", displayName\)/);
assert.match(masterCacheKeys, /ClearBootstrapPayload\(cache, departmentId\)/);
assert.match(masterCacheKeys, /ClearPricingPlan/);
assert.doesNotMatch(
    businessDayRepository,
    /ClearNominationBacks/,
    '営業日開始・締めで静的な指名バックマスタを削除しないでください。'
);
