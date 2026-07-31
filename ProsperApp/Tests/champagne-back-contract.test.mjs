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
const operationalSql = read('Sql/store_rpc/14_operational_read_models.sql');
const guardsSql = read('Sql/store_rpc/13_accounting_snapshot_guards.sql');
const edgeSource = read('supabase/functions/prosper-rpc/index.ts');
const pageSource = read('Pages/Closing/ChampagneBacks.cshtml');
const closingPageSource = read('Pages/Closing/Index.cshtml');

assert.match(schema, /create table if not exists public\.store_business_day_champagne_backs/i);
assert.match(
    schema,
    /constraint uq_store_business_day_champagne_backs_day_cast unique \(business_day_id, cast_id\)/i
);
assert.match(schema, /back_type text not null default 'champagne'/i);
assert.match(schema, /alter table public\.store_business_day_champagne_backs enable row level security/i);

const getStatus = functionBlock(operationalSql, 'store.get_business_day_champagne_back_status');
assert.match(getStatus, /attendance_status in \('scheduled', 'checked_in', 'checked_out'\)/i);
assert.match(getStatus, /cb\.status = 'active'/i);

const saveBacks = functionBlock(operationalSql, 'store.save_business_day_champagne_backs');
assert.match(saveBacks, /from public\.store_business_days bd[\s\S]*for update/i);
assert.match(saveBacks, /champagne_back_attendance_changed/i);
assert.match(saveBacks, /on conflict \(business_day_id, cast_id\) do update/i);
assert.match(saveBacks, /back_amount'\)\:\:numeric/i);

const readiness = functionBlock(businessDaySql, 'store.get_business_day_closing_readiness');
assert.match(readiness, /champagne_back_missing_cast_count/i);
assert.match(readiness, /シャンパンバックが未入力です。0円の場合も保存してください。/);

const closeBusinessDay = functionBlock(businessDaySql, 'store.close_business_day');
assert.match(closeBusinessDay, /champagne_back_required/i);

assert.match(guardsSql, /'store_business_day_champagne_backs'/i);
assert.match(guardsSql, /'champagne_back_total_amount'/i);
assert.match(guardsSql, /'champagne_backs'/i);

assert.match(edgeSource, /"store\.get_business_day_champagne_back_overview"/);
assert.match(edgeSource, /"store\.save_business_day_champagne_backs"/);
assert.match(pageSource, /シャンパンバック入力/);
assert.match(pageSource, /0 円で保存してください/);
assert.match(closingPageSource, /data-closing-panel="champagneBack"/);
