import assert from 'node:assert/strict';
import { access, readFile } from 'node:fs/promises';

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');
const exists = async (path) => {
    try {
        await access(new URL(`../${path}`, import.meta.url));
        return true;
    } catch {
        return false;
    }
};
const [migration, sql, page, pageModel, repository, source, program] = await Promise.all([
    read('Sql/store_rpc/00a_drink_back_schema.sql'),
    read('Sql/store_rpc/30_current_drink_back_adjustments.sql'),
    read('Pages/Closing/Index.cshtml'),
    read('Pages/Closing/Index.DrinkBack.cs'),
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
assert.match(pageModel, /OnGetDrinkBackEditorAsync/);
assert.match(pageModel, /OnPostDrinkBackSaveAsync/);
assert.equal(await exists('Pages/Closing/DrinkBacks.cshtml'), false, '旧ドリンクバックページを残さないこと');
assert.equal(await exists('Pages/Closing/DrinkBacks.cshtml.cs'), false, '旧ドリンクバックPageModelを残さないこと');
assert.match(repository, /store\.get_current_drink_back_editor/);
assert.match(repository, /store\.save_drink_back_adjustments_v2/);
assert.match(source, /sessionStorage\.getItem\(pendingStorageKey\)/);
assert.match(source, /pendingCommand \?\?= collectCommand\(\)/);
assert.match(source, /if \(!pendingCommand \|\| saving\) return/);
assert.match(source, /className = 'drink-back-editor__department'/, 'キャスト名と店名を別列表示すること');
assert.match(source, /input\.type = 'text'/, 'ドリンクバック金額入力はnumber spinnerを出さないこと');
assert.doesNotMatch(source, /row\.isSaved \? '保存済み' : '未保存'/, '一括保存画面に行別保存状態を表示しないこと');
assert.doesNotMatch(source, /未入力 \$\{editor\.status\.missingCastCount\}名/, '一括保存画面に未入力件数ナビを表示しないこと');
assert.doesNotMatch(source, /保存済み \$\{savedOptionalCount\}名/, '一括保存画面に任意調整の保存件数を表示しないこと');
assert.doesNotMatch(page, /data-drink-back-required-status|data-drink-back-optional-badge/, '不要な件数表示領域を描画しないこと');
assert.match(program, /AddScoped<IDrinkBackRepository, SupabaseDrinkBackRepository>/);

console.log('Drink-back v2 contract checks passed.');
