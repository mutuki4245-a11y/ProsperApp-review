import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [sql, repository, pageModel, closeSource] = await Promise.all([
    readFile(new URL('../Sql/store_rpc/12_daily_report.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Infrastructure/Supabase/SupabaseDailyReportRepository.cs', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Closing/Index.cshtml.cs', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/daily-report.js', import.meta.url), 'utf8')
]);
const closingCss = await readFile(new URL('../wwwroot/css/features/closing.css', import.meta.url), 'utf8');

assert.match(sql, /create or replace function store\.build_business_day_daily_report/i);
assert.match(sql, /create or replace function store\.get_business_day_daily_report/i);
assert.match(sql, /store_business_day_closing_snapshots/i);
assert.match(sql, /daily_report_closing_snapshot_not_found/i);
assert.match(repository, /store\.get_business_day_daily_report/);
assert.match(pageModel, /OnGetDailyReportAsync/);
assert.match(closeSource, /prosper:daily-report-business-day/);
assert.match(closeSource, /url\.searchParams\.set\('businessDayId'/);
assert.match(closeSource, /daily-report-preview-scale/, '画面用の縮小率をJSで設定すること');
assert.match(closeSource, /window\.addEventListener\('resize', schedulePreviewLayout\)/, '画面幅変更で日報プレビューを再縮小すること');
assert.match(closingCss, /\.daily-report__pages\s*\{[\s\S]*flex-direction: row/, '画面では日報3ページを横並びにすること');
assert.match(closingCss, /@media print[\s\S]*\.daily-report__pages\s*\{[\s\S]*display: block[\s\S]*transform: none !important/, '印刷時は画面用縮小を解除すること');

console.log('Daily report read-model contract checks passed.');
