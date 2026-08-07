import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');
const [schema, checkoutCore, checkoutV2, businessDay, guards, order] = await Promise.all([
    read('Sql/store_order_accounting_tables.sql'),
    read('Sql/store_rpc/08_checkout_ready.sql'),
    read('Sql/store_rpc/26_current_checkout_mutations.sql'),
    read('Sql/store_rpc/01_business_day.sql'),
    read('Sql/store_rpc/13_accounting_snapshot_guards.sql'),
    read('Sql/store_rpc_functions.sql')
]);

const functionBlock = (source, name) => {
    const match = new RegExp(`create\\s+or\\s+replace\\s+function\\s+${name.replace('.', '\\.')}\\b[\\s\\S]*?\\$\\$;`, 'i').exec(source);
    assert.ok(match, `${name} の定義が必要です。`);
    return match[0];
};

assert.match(schema, /create table if not exists public\.store_slip_accounting_snapshots/i);
assert.match(schema, /create table if not exists public\.store_business_day_closing_snapshots/i);
assert.match(schema, /ux_store_slip_accounting_snapshots_active[\s\S]*where status = 'active'/i);

const issueInternal = functionBlock(checkoutCore, 'store.issue_checkout_statement_internal');
assert.match(issueInternal, /store\.capture_slip_accounting_snapshot/i);
assert.match(issueInternal, /store\.get_business_day_snapshot_internal/i);

const releaseInternal = functionBlock(checkoutCore, 'store.release_checkout_ready_internal');
assert.match(releaseInternal, /store\.invalidate_slip_accounting_snapshot/i);
const cancelInternal = functionBlock(await read('Sql/store_rpc/05_checkout.sql'), 'store.cancel_checkout_internal');
assert.match(cancelInternal, /store\.invalidate_slip_accounting_snapshot/i);

const applyV2 = functionBlock(checkoutV2, 'store.apply_checkout_mutation_v2');
for (const dependency of [
    'store.issue_checkout_statement_internal',
    'store.release_checkout_ready_internal',
    'store.confirm_checkout_draft_v2',
    'store.cancel_checkout_internal'
]) assert.match(applyV2, new RegExp(dependency.replace('.', '\\.')));
assert.match(applyV2, /current_business_day_operation_results/i);
assert.match(applyV2, /business_day_operation_id_reused/i);

for (const name of ['issue_checkout_statement_v2', 'release_checkout_ready_v2', 'confirm_checkout_v2', 'cancel_checkout_v2']) {
    const block = functionBlock(checkoutV2, `store.${name}`);
    assert.match(block, /p_operation_id text/i);
    assert.match(block, /p_expected_business_day_revision bigint/i);
    assert.match(block, /store\.apply_checkout_mutation_v2/i);
}

const closeInternal = functionBlock(businessDay, 'store.close_business_day_internal');
assert.match(closeInternal, /store\.capture_business_day_closing_snapshot/i);
assert.match(guards, /create or replace function store\.guard_closed_business_day_mutation/i);
assert.ok(order.indexOf('08_checkout_ready.sql') < order.indexOf('26_current_checkout_mutations.sql'));
assert.ok(order.indexOf('13_accounting_snapshot_guards.sql') < order.indexOf('22_current_business_day_close.sql'));

console.log('Accounting snapshot v2 contract checks passed.');
