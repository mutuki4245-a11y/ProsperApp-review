import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

const schema = read('Sql/store_order_accounting_tables.sql');
const businessDaySql = read('Sql/store_rpc/01_business_day.sql');
const masterSql = read('Sql/store_rpc/02_store_masters.sql');
const cancelSql = read('Sql/store_rpc/05_checkout.sql');
const adjustmentSql = read('Sql/store_rpc/07_cast_sales_adjustments.sql');
const checkoutSql = read('Sql/store_rpc/08_checkout_ready.sql');
const businessHomeSql = read('Sql/store_rpc/09_business_home_snapshot.sql');
const guardsSql = read('Sql/store_rpc/13_accounting_snapshot_guards.sql');
const rpcOrder = read('Sql/store_rpc_functions.sql');

const functionBlock = (source, name) => {
    const escapedName = name.replace('.', '\\.');
    const match = new RegExp(
        `create\\s+or\\s+replace\\s+function\\s+${escapedName}\\b[\\s\\S]*?\\$\\$;`,
        'i'
    ).exec(source);
    assert.ok(match, `${name} の定義が必要です。`);
    return match[0];
};

assert.match(schema, /create table if not exists public\.store_slip_accounting_snapshots/i);
assert.match(schema, /create table if not exists public\.store_business_day_closing_snapshots/i);
assert.match(
    schema,
    /create unique index if not exists ux_store_slip_accounting_snapshots_active[\s\S]*where status = 'active'/i
);
assert.match(
    schema,
    /create unique index if not exists ux_store_slip_cast_sales_adjustments_confirmed[\s\S]*where status = 'confirmed'/i
);
assert.match(schema, /backfilled boolean not null default false/i);
assert.match(schema, /alter table public\.store_slip_accounting_snapshots enable row level security/i);
assert.match(schema, /alter table public\.store_business_day_closing_snapshots enable row level security/i);

const issueCheckout = functionBlock(checkoutSql, 'store.issue_checkout_statement');
assert.match(issueCheckout, /store\.capture_slip_accounting_snapshot/i);
assert.match(issueCheckout, /store\.get_business_day_snapshot/i);
assert.match(issueCheckout, /v_print_data[\s\S]*v_review_data[\s\S]*v_business_home_data/i);

const getStatement = functionBlock(checkoutSql, 'store.get_checkout_statement_print_data');
assert.match(getStatement, /from public\.store_slip_accounting_snapshots/i);
assert.doesNotMatch(getStatement, /store\.build_checkout_(?:statement|review)_data/i);

const confirmCheckout = functionBlock(checkoutSql, 'store.confirm_checkout');
assert.match(confirmCheckout, /select ss\.print_data[\s\S]*from public\.store_slip_accounting_snapshots/i);
assert.match(confirmCheckout, /v_statement_data->>'total_amount'/i);
assert.doesNotMatch(confirmCheckout, /round\s*\(\s*v_subtotal_amount\s*\*\s*0\.20/i);

const releaseCheckout = functionBlock(checkoutSql, 'store.release_checkout_ready');
assert.match(releaseCheckout, /store\.invalidate_slip_accounting_snapshot/i);
assert.match(releaseCheckout, /update public\.store_slip_pricing_lines/i);
assert.match(releaseCheckout, /source_type = 'automatic_pricing'/i);
assert.match(releaseCheckout, /from public\.store_slips[\s\S]*for update/i);

const cancelCheckout = functionBlock(cancelSql, 'store.cancel_checkout');
assert.match(cancelCheckout, /store\.invalidate_slip_accounting_snapshot/i);
assert.match(cancelCheckout, /update public\.store_slip_pricing_lines/i);
assert.match(cancelCheckout, /source_type = 'automatic_pricing'/i);
assert.match(cancelCheckout, /from public\.store_slips[\s\S]*for update/i);
assert.match(cancelCheckout, /update public\.store_slip_cast_sales_adjustments[\s\S]*status = 'cancelled'/i);
assert.doesNotMatch(cancelCheckout, /delete from public\.store_slip_cast_sales_adjustments/i);

const saveAdjustments = functionBlock(adjustmentSql, 'store.save_cast_sales_adjustment');
assert.match(saveAdjustments, /from public\.store_slips[\s\S]*for update/i);
assert.match(saveAdjustments, /update public\.store_slip_cast_sales_adjustments[\s\S]*status = 'cancelled'/i);
assert.doesNotMatch(saveAdjustments, /delete from public\.store_slip_cast_sales_adjustments/i);

const closeBusinessDay = functionBlock(businessDaySql, 'store.close_business_day');
assert.match(closeBusinessDay, /store\.capture_business_day_closing_snapshot/i);
assert.match(closeBusinessDay, /closed_at = v_closed_at/i);
assert.match(closeBusinessDay, /from public\.store_business_days[\s\S]*for update/i);

const getBusinessSnapshot = functionBlock(businessHomeSql, 'store.get_business_day_snapshot_at');
assert.match(getBusinessSnapshot, /left join public\.store_slip_accounting_snapshots/i);
assert.match(getBusinessSnapshot, /when sp\.status <> 'open' and sp\.business_home_data is not null/i);
assert.match(getBusinessSnapshot, /else sp\.computed_slip/i);
assert.match(getBusinessSnapshot, /calculate_slip_pricing\(s\.department_id, s\.slip_id, p_as_of\)/i);
assert.doesNotMatch(getBusinessSnapshot, /calculate_slip_pricing\(s\.department_id, s\.slip_id, now\(\)\)/i);

assert.match(guardsSql, /create or replace function store\.guard_closed_business_day_mutation/i);
assert.match(guardsSql, /for key share/i);
assert.match(guardsSql, /business_day_slip_mismatch/i);
assert.match(guardsSql, /backfilled_from_checkout/i);
assert.match(guardsSql, /get_business_day_snapshot_at[\s\S]*p_closed_at/i);
for (const tableName of [
    'store_slips',
    'store_slip_customers',
    'store_slip_casts',
    'store_order_lines',
    'store_checkouts',
    'store_checkout_payments',
    'store_slip_pricing_lines',
    'store_slip_accounting_snapshots'
]) {
    assert.match(guardsSql, new RegExp(`'${tableName}'`, 'i'), `${tableName} に締め後ガードが必要です。`);
}
assert.match(guardsSql, /trg_department_master_identity_immutable/i);
assert.match(guardsSql, /trg_cast_master_identity_immutable/i);
assert.match(
    guardsSql,
    /trg_department_master_identity_immutable[\s\S]*before update or delete on public\.department_master/i
);
assert.doesNotMatch(
    guardsSql,
    /'store_business_home_flush_batches'/i,
    '7日保持の再送判定行は締め後ガード対象にしません。'
);

const deleteItem = functionBlock(masterSql, 'store.delete_item');
assert.match(deleteItem, /set is_active = false/i);
assert.doesNotMatch(deleteItem, /delete from public\.store_item_master/i);
assert.doesNotMatch(deleteItem, /set item_id = null/i);

const pricingOrder = rpcOrder.indexOf('Sql/store_rpc/11_pricing.sql');
const checkoutOrder = rpcOrder.indexOf('Sql/store_rpc/08_checkout_ready.sql');
const snapshotGuardOrder = rpcOrder.indexOf('Sql/store_rpc/13_accounting_snapshot_guards.sql');
const grantsOrder = rpcOrder.indexOf('Sql/store_rpc/99_grants.sql');
assert.ok(pricingOrder >= 0 && pricingOrder < checkoutOrder, '料金定義を会計RPCより先に適用してください。');
assert.ok(checkoutOrder < snapshotGuardOrder, '会計RPCをbackfillと締め後ガードより先に適用してください。');
assert.ok(snapshotGuardOrder < grantsOrder, '権限剥奪は最後に適用してください。');
