import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const walk = (relativePath, extension) => fs.readdirSync(path.join(root, relativePath), { withFileTypes: true })
    .flatMap((entry) => {
        const entryPath = path.join(relativePath, entry.name);
        return entry.isDirectory() ? walk(entryPath, extension) : entry.name.endsWith(extension) ? [entryPath] : [];
    });
const sorted = (values) => [...values].sort();
const difference = (left, right) => sorted([...left].filter((value) => !right.has(value)));

const repositorySource = walk('Infrastructure/Supabase', '.cs').map(read).join('\n');
const csharpRpcNames = new Set([...repositorySource.matchAll(
    /(?:PostRpcArrayAsync|PostRpcArrayResultAsync|PostRpcScalarResultAsync|PostArrayAsync|PostScalarAsync|ExecuteMutationAsync|ExecuteMutationV2Async)\s*\(\s*"(store\.[a-z0-9_]+)"/g
)].map((match) => match[1]));

const edgeSource = read('supabase/functions/prosper-rpc/index.ts');
const edgeRpcNames = new Set([...edgeSource.matchAll(/"(store\.[a-z0-9_]+)"\s*,\s*\{/g)].map((match) => match[1]));
const sqlSource = ['Sql/store_settings_functions.sql', ...walk('Sql/store_rpc', '.sql')].map(read).join('\n');
const sqlDefinitions = [...sqlSource.matchAll(/create\s+or\s+replace\s+function\s+(store\.[a-z0-9_]+)/gi)]
    .map((match) => match[1].toLowerCase());
const sqlRpcNames = new Set(sqlDefinitions);

assert.deepEqual(difference(csharpRpcNames, edgeRpcNames), [], 'C# RPCはEdge allowlistへ全て登録すること。');
assert.deepEqual(difference(edgeRpcNames, csharpRpcNames), [], '未使用RPCをEdge allowlistへ残さないこと。');
assert.deepEqual(difference(csharpRpcNames, sqlRpcNames), [], 'C# RPCにはSQL定義が必要です。');
assert.deepEqual(
    sorted([...new Set(sqlDefinitions.filter((name, index) => sqlDefinitions.indexOf(name) !== index))]),
    [],
    '同名関数を複数ファイルで再定義しないこと。'
);

for (const rpcName of csharpRpcNames) {
    const block = new RegExp(
        `create\\s+or\\s+replace\\s+function\\s+${rpcName.replace('.', '\\.')}\\b[\\s\\S]*?\\$\\$;`,
        'i'
    ).exec(sqlSource)?.[0];
    assert.ok(block, `${rpcName} のSQL定義を読み取れません。`);
    assert.match(block, /\bsecurity\s+definer\b/i, `${rpcName} はsecurity definerで統一すること。`);
    assert.match(block, /\bset\s+search_path\s*=\s*(?:pg_catalog\s*,\s*)?public\b/i, `${rpcName} はsearch_pathを固定すること。`);
}

for (const required of [
    'store.get_business_home_bootstrap_v2',
    'store.get_current_business_home_snapshot',
    'store.sync_business_home_changes_v2',
    'store.submit_current_order_entry_v2',
    'store.save_current_business_day_attendance_v2',
    'store.get_current_closing_dashboard',
    'store.close_business_day_v2',
    'store.save_current_business_day_drink_delivery_amount_v2',
    'store.save_current_cast_sales_adjustment_v2',
    'store.save_drink_back_adjustments_v2',
    'store.get_current_receipt_work_queue',
    'store.advance_receipt_work_queue_v2',
    'store.get_management_master_snapshot',
    'store.save_management_master_v2'
]) assert.ok(csharpRpcNames.has(required), `${required} をアプリ契約に含めること。`);

const legacyRpcNames = [
    'store.get_store_bootstrap',
    'store.get_context',
    'store.get_current_business_day',
    'store.open_business_day',
    'store.open_business_day_with_attendance',
    'store.save_business_day_attendance',
    'store.save_business_day_staff_attendance',
    'store.get_business_day_closing_attendance',
    'store.save_business_day_closing_attendance',
    'store.save_business_day_staff_closing_attendance',
    'store.get_business_day_closing_readiness',
    'store.close_business_day',
    'store.get_business_day_drink_delivery_status',
    'store.save_business_day_drink_delivery_amount',
    'store.get_order_attending_casts',
    'store.get_order_entry_slips',
    'store.add_order_lines',
    'store.create_slip',
    'store.cancel_checkout',
    'store.issue_checkout_statement',
    'store.release_checkout_ready',
    'store.confirm_checkout',
    'store.get_business_day_snapshot',
    'store.flush_business_home_changes',
    'store.flush_current_business_home_changes',
    'store.get_pending_receipts',
    'store.quick_enter_receipt',
    'store.mark_receipt_scan_mistake',
    'store.get_business_day_champagne_back_overview',
    'store.save_business_day_champagne_backs',
    'store.get_business_day_cast_sales_adjustment_overview',
    'store.save_business_day_cast_sales_adjustments',
    'store.save_pricing_plan',
    'store.save_pricing_plan_v2'
];
for (const legacy of legacyRpcNames) {
    assert.ok(!csharpRpcNames.has(legacy), `${legacy} をC#呼出しに残さないこと。`);
    assert.ok(!edgeRpcNames.has(legacy), `${legacy} をEdge allowlistに残さないこと。`);
    assert.ok(!sqlRpcNames.has(legacy), `${legacy} をSQL関数定義として残さないこと。`);
}

const cutover = read('Sql/store_rpc/00_legacy_rpc_cutover.sql');
for (const legacy of legacyRpcNames) {
    assert.match(cutover, new RegExp(`drop\\s+function\\s+if\\s+exists\\s+${legacy.replace('.', '\\.')}`, 'i'));
}
const order = read('Sql/store_rpc_functions.sql');
assert.ok(order.indexOf('00_legacy_rpc_cutover.sql') < order.indexOf('00a_drink_back_schema.sql'));
assert.ok(order.indexOf('17_current_business_home_snapshot.sql') < order.indexOf('15_business_home_bootstrap.sql'));
assert.ok(order.indexOf('28_current_closing_dashboard.sql') < order.indexOf('22_current_business_day_close.sql'));
assert.ok(order.indexOf('22_current_business_day_close.sql') < order.indexOf('99_grants.sql'));

const grants = read('Sql/store_rpc/99_grants.sql');
assert.match(grants, /revoke\s+execute\s+on\s+all\s+functions\s+in\s+schema\s+store\s+from\s+public,\s*anon,\s*authenticated,\s*service_role/i);
assert.match(grants, /alter\s+default\s+privileges\s+in\s+schema\s+store\s+revoke\s+execute\s+on\s+functions\s+from\s+public/i);

const businessHomeBootstrap = read('Sql/store_rpc/15_business_home_bootstrap.sql');
assert.match(
    businessHomeBootstrap,
    /to_jsonb\(table_row\)\s*-\s*'sort_order'\s*-\s*'is_active'[\s\S]*?from\s+store\.get_table_admin_list\(p_department_id\)\s+table_row[\s\S]*?where\s+table_row\.is_active\s*=\s*true/i,
    '営業中bootstrapは管理用テーブル順を使い、返却JSONから管理用列を除くこと。'
);

console.log('RPC v2 contract checks passed.');
