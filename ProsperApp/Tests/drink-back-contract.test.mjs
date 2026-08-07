import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');
const [migration, sql, page, pageModel, repository, source, program] = await Promise.all([
    read('Sql/store_rpc/00a_drink_back_schema.sql'),
    read('Sql/store_rpc/30_current_drink_back_adjustments.sql'),
    read('Pages/Closing/DrinkBacks.cshtml'),
    read('Pages/Closing/DrinkBacks.cshtml.cs'),
    read('Infrastructure/Supabase/SupabaseDrinkBackRepository.cs'),
    read('wwwroot/js/features/drink-back-editor.js'),
    read('Program.cs')
]);

assert.match(migration, /create table if not exists public\.store_business_day_drink_back_adjustments/i);
assert.match(migration, /from public\.store_business_day_champagne_backs legacy/i);
assert.match(migration, /on conflict \(business_day_id, cast_id\) do update/i);
assert.match(migration, /drop table public\.store_business_day_champagne_backs/i);
assert.match(sql, /create or replace function store\.get_current_drink_back_editor/i);
assert.match(sql, /create or replace function store\.save_drink_back_adjustments_v2/i);
assert.match(sql, /p_operation_id text/i);
assert.match(sql, /p_expected_business_day_revision bigint/i);
assert.match(sql, /current_business_day_operation_results/i);
assert.match(page, /data-drink-back-editor/);
assert.match(pageModel, /OnGetEditorAsync/);
assert.match(pageModel, /OnPostSaveAsync/);
assert.doesNotMatch(pageModel, /RedirectToPage/);
assert.match(repository, /store\.get_current_drink_back_editor/);
assert.match(repository, /store\.save_drink_back_adjustments_v2/);
assert.match(source, /sessionStorage\.getItem\(pendingStorageKey\)/);
assert.match(source, /pendingCommand \?\?= collectCommand\(\)/);
assert.match(source, /if \(!pendingCommand \|\| saving\) return/);
assert.match(program, /AddScoped<IDrinkBackRepository, SupabaseDrinkBackRepository>/);

console.log('Drink-back v2 contract checks passed.');
