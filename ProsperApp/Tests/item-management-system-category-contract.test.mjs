import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [page, pageModel, editor, schema, pricingItems, managementSql] = await Promise.all([
    readFile(new URL('../Pages/Management/Items.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Management/Items.cshtml.cs', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/management-editor.js', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_order_accounting_tables.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/12_pricing_system_items.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/16_management_master_snapshot.sql', import.meta.url), 'utf8')
]);

assert.match(page, /data-management-editor/);
assert.match(page, /management-editor\.js/);
assert.match(pageModel, /IActionResult OnGet\(\)/);
assert.doesNotMatch(pageModel, /OnPost|RedirectToPage|IStoreItemAdminRepository/);
assert.match(editor, /String\(item\.item_type \?\? 'standard'\) !== 'standard'/);
assert.match(schema, /item_type text not null default 'standard'/i);
assert.match(schema, /item_type in \('standard', 'karaoke', 'nomination_fee', 'set_fee', 'extension_fee'\)/i);
assert.match(pricingItems, /create or replace function store\.ensure_pricing_system_items/i);
assert.match(managementSql, /when 'items'/i);
assert.match(managementSql, /store\.upsert_item_internal/i);

console.log('Item management shell and system-category checks passed.');
