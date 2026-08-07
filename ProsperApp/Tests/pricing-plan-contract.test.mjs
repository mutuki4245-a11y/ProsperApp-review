import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [pricing, management, editor] = await Promise.all([
    readFile(new URL('../Sql/store_rpc/11_pricing.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/16_management_master_snapshot.sql', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/management-editor.js', import.meta.url), 'utf8')
]);

assert.match(pricing, /extension_minutes integer not null default 30/i);
assert.match(pricing, /create or replace function store\.save_pricing_plan_internal/i);
assert.match(pricing, /p_extension_minutes integer/i);
assert.doesNotMatch(pricing, /create or replace function store\.save_pricing_plan(?:_v2)?\b/i);
assert.match(management, /when 'pricing'/i);
assert.match(management, /store\.save_pricing_plan_internal/i);
assert.match(management, /p_payload->>'extension_minutes'/i);
assert.match(editor, /extension_minutes/);

console.log('Pricing plan management-v2 checks passed.');
