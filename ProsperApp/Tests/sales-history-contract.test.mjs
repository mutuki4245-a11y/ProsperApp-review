import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');
const [sql, edge, repository, service, pageModel, layout, settings, view] = await Promise.all([
    read('Sql/store_rpc/31_sales_history.sql'),
    read('supabase/functions/prosper-rpc/index.ts'),
    read('Infrastructure/Supabase/SupabaseSalesHistoryRepository.cs'),
    read('Features/SalesHistory/SalesHistoryApplicationService.cs'),
    read('Pages/SalesHistory/Index.cshtml.cs'),
    read('Pages/Shared/_Layout.cshtml'),
    read('appsettings.json'),
    read('Pages/SalesHistory/Index.cshtml')
]);

for (const rpc of [
    'store.get_sales_history_page',
    'store.get_sales_history_correction_editor',
    'store.save_sales_history_correction_v1'
]) {
    assert.match(sql, new RegExp(`create or replace function ${rpc.replace('.', '\\.')}`, 'i'));
    assert.match(edge, new RegExp(`"${rpc.replace('.', '\\.')}"`));
    assert.match(repository, new RegExp(rpc.replace('.', '\\.')));
    assert.match(sql, new RegExp(`revoke all on function ${rpc.replace('.', '\\.')}`, 'i'));
}

assert.match(sql, /row_number\(\) over\s*\(\s*order by day\.business_date desc, day\.business_day_id desc/i, '直近3件は営業日レコード順位で判定すること');
assert.match(sql, /day\.business_day_rank <= 3/i);
assert.match(sql, /\(day\.business_date, day\.business_day_id\)\s*<\s*\(p_before_business_date/i, 'OFFSETではなく複合カーソルを使うこと');
assert.match(sql, /where status in \('open', 'closed'\)/i, '履歴用部分インデックスを持つこと');
assert.match(sql, /snapshot\.closing_data#>>'\{daily_report,schemaVersion\}' = 'daily-report-v3'/i, '旧形式は補正対象外にすること');
assert.match(sql, /when day\.status = 'open' then '営業中の編集は締め作業で行ってください。'/i);
assert.match(sql, /'correction', p_correction/i);
assert.match(sql, /'actor_email', v_actor_email/i);
assert.match(sql, /'reason', v_reason/i);
assert.match(sql, /'source_snapshot_version', v_snapshot\.snapshot_version/i);
assert.match(sql, /v_status := 'unchanged'/i);
assert.match(sql, /pg_advisory_xact_lock\(/i);
assert.match(sql, /for update/i);
assert.match(sql, /store\.sales_history_correction_business_day_id/i);
assert.doesNotMatch(sql, /set_config\(\s*'store\.allow_closed_day_mutation'/i, '補正で全営業日解除フラグを使わないこと');

assert.match(edge, /p_before_business_date", type: "date"/);
assert.match(edge, /p_expected_snapshot_version", type: "integer"/);
assert.match(edge, /p_correction_payload", type: "jsonb"/);
assert.match(service, /reason\.Length > 500/i);
assert.match(pageModel, /FindFirstValue\(ClaimTypes\.Email\)/, '操作者は認証Claimから取得すること');
assert.match(pageModel, /_storeClock\.GetCurrentBusinessDate\(\)/, '現在営業日はサーバー時計から取得すること');
assert.match(layout, /asp-page="\/SalesHistory\/Index"/);
assert.match(settings, /"SalesHistory"/);
assert.match(view, /data-sales-history/);
assert.match(view, /data-history-page-url/);
assert.match(view, /data-correction-editor-url/);
assert.match(view, /data-correction-save-url/);
assert.match(view, /基本情報/);
assert.match(view, /勤怠/);
assert.match(view, /ドリンクバック/);
assert.match(view, /キャスト売上/);
assert.match(view, /maxlength="500"/);

console.log('Sales history contract checks passed.');
