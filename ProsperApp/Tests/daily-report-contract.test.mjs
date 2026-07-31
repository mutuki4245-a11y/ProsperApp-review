import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const functionBlock = (source, name) => {
    const escapedName = name.replace('.', '\\.');
    const match = new RegExp(
        `create\\s+or\\s+replace\\s+function\\s+${escapedName}\\b[\\s\\S]*?\\$\\$;`,
        'i'
    ).exec(source);
    assert.ok(match, `${name} の定義が必要です。`);
    return match[0];
};

const schema = read('Sql/store_order_accounting_tables.sql');
const businessDaySql = read('Sql/store_rpc/01_business_day.sql');
const receiptSql = read('Sql/store_rpc/06_receipts.sql');
const reportSql = read('Sql/store_rpc/12_daily_report.sql');
const guardsSql = read('Sql/store_rpc/13_accounting_snapshot_guards.sql');
const edgeSource = read('supabase/functions/prosper-rpc/index.ts');
const closingPage = read('Pages/Closing/Index.cshtml');
const receiptPage = read('Pages/Closing/Receipts.cshtml');
const receiptModel = read('Pages/Closing/Receipts.cshtml.cs');
const reportScript = read('wwwroot/js/features/daily-report.js');
const reportStyles = read('wwwroot/css/features/closing.css');

assert.match(schema, /create table if not exists public\.store_business_day_cast_advances/i);
assert.match(schema, /journal_entry_id uuid not null references accounting\.journal_entries/i);
assert.match(schema, /alter table public\.store_business_day_cast_advances enable row level security/i);
assert.match(guardsSql, /'store_business_day_cast_advances'/i);
assert.match(
    guardsSql,
    /revoke all on table public\.store_business_day_cast_advances from public, anon, authenticated, service_role/i
);

const quickEntry = functionBlock(receiptSql, 'store.quick_enter_receipt');
assert.match(quickEntry, /p_business_day_id bigint default null/i);
assert.match(quickEntry, /p_advance_cast_id bigint default null/i);
assert.match(quickEntry, /attendance_status in \('scheduled', 'checked_in', 'checked_out'\)/i);
assert.match(quickEntry, /cast_advance_business_day_not_open/i);
assert.match(quickEntry, /insert into public\.store_business_day_cast_advances/i);
assert.ok(
    quickEntry.indexOf('accounting.save_journal_payload') <
        quickEntry.indexOf('insert into public.store_business_day_cast_advances'),
    '仕訳保存後、同一トランザクションでキャスト前渡金を紐付けてください。'
);

const buildReport = functionBlock(reportSql, 'store.build_business_day_daily_report');
assert.match(buildReport, /checkout\.status = 'confirmed'/i);
assert.match(buildReport, /from public\.payment_method_master master/i);
assert.match(buildReport, /entry\.entry_date = v_business_day\.business_date/i);
assert.match(buildReport, /accounting\.document_journal_links/i);
assert.match(buildReport, /'cashBalanceAmount', v_cash_total - v_expense_total/i);
assert.match(buildReport, /'drinkDeliveryAmount', v_business_day\.drink_delivery_amount/i);
assert.match(buildReport, /store_business_day_cast_advances/i);
assert.match(buildReport, /'itemCategories'/i);
assert.match(buildReport, /'visits'/i);

const getReport = functionBlock(reportSql, 'store.get_business_day_daily_report');
assert.match(getReport, /closing_data->'daily_report'/i);
assert.match(getReport, /daily-report-legacy-v1/i);
assert.match(getReport, /legacyUnavailableSections/i);
assert.match(getReport, /'expenses', 'castAdvances', 'itemCategories'/i);

const capture = functionBlock(guardsSql, 'store.capture_business_day_closing_snapshot');
assert.match(capture, /business-day-closing-v2/i);
assert.match(capture, /store\.build_business_day_daily_report/i);
assert.match(capture, /'daily_report', v_daily_report/i);

const closeBusinessDay = functionBlock(businessDaySql, 'store.close_business_day');
assert.ok(
    closeBusinessDay.indexOf('set memo =') <
        closeBusinessDay.indexOf('store.capture_business_day_closing_snapshot') &&
        closeBusinessDay.indexOf('store.capture_business_day_closing_snapshot') <
            closeBusinessDay.indexOf("set status = 'closed'"),
    '締めメモを確定してから日報を固定し、その後で営業日をclosedへ変更してください。'
);

assert.match(edgeSource, /"store\.get_business_day_daily_report"/);
assert.match(edgeSource, /\{ name: "p_business_day_id", type: "bigint" \}/);
assert.match(edgeSource, /\{ name: "p_advance_cast_id", type: "bigint" \}/);

assert.match(receiptPage, /data-cast-advance-select/);
assert.match(receiptModel, /CastAdvanceAccountSubject = "前渡金: キャスト"/);
assert.match(receiptModel, /GetClosingAttendanceAsync/i);
assert.match(receiptModel, /Input\.PaymentDate != CurrentBusinessDay\.BusinessDate/i);

assert.match(closingPage, /data-daily-report/);
assert.match(closingPage, /data-report-print/);
assert.match(reportScript, /refreshIntervalMs = 30000/);
assert.match(reportScript, /window\.print\(\)/);
assert.match(reportStyles, /size: A4 portrait/i);
assert.match(reportStyles, /\.daily-report,[\s\S]*\.daily-report \*/i);
