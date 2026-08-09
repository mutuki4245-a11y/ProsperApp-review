import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [
    createSlipSql,
    businessHomeSnapshotSql,
    dailyReportSql,
    dailyReportModel,
    dailyReportSource,
    businessHomeSource,
    receiptPrinterSource
] = await Promise.all([
    readFile(new URL('../Sql/store_rpc/05_checkout.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/09_business_home_snapshot.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/12_daily_report.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Features/DailyReports/DailyReportModels.cs', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/daily-report.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-home.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/sii-receipt-printer.js', import.meta.url), 'utf8')
]);

assert.doesNotMatch(createSlipSql, /\bv_slip_no\b/, '新規伝票で人間向けslip_noを生成しないこと');
assert.doesNotMatch(createSlipSql, /'S'\s*\|\|\s*to_char\(p_opened_at/i, '時刻ベースの伝票番号を生成しないこと');
assert.doesNotMatch(createSlipSql, /slip_no\s*,[\s\S]*?values\s*\([\s\S]*?v_slip_no/i, 'store_slips作成時にslip_noを保存しないこと');
assert.doesNotMatch(businessHomeSnapshotSql, /'slipNo'/, '営業中スナップショットへslipNoを含めないこと');

assert.match(dailyReportSource, /visit\.slipId \? String\(visit\.slipId\)/, '日報No列はslip_idを表示すること');
assert.doesNotMatch(dailyReportSource, /visit\.slipNo\s*\|\|/, '日報No列でslip_noを優先しないこと');
assert.doesNotMatch(dailyReportSql, /'slipNo'/, '新規日報JSONへslipNoを含めないこと');
assert.doesNotMatch(dailyReportModel, /SlipNo/, '日報モデルへSlipNoを戻さないこと');

assert.match(businessHomeSource, /const value = String\(slip\?\.id \|\| ''\)/, '伝票詳細の番号表示はslip_idを使うこと');
assert.doesNotMatch(businessHomeSource, /Number\(left\.slipNo\)/, '伝票の並び順にslip_noを使わないこと');
assert.match(receiptPrinterSource, /compact\(request\?\.slipId, request\?\.slip_id, request\?\.slipNo\)/, 'レシート説明文はslip_idを優先すること');

console.log('Slip ID display contract checks passed.');
