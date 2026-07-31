import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

const itemPage = read('Pages/Management/Items.cshtml');
const itemPageModel = read('Pages/Management/Items.cshtml.cs');
const schemaSql = read('Sql/store_order_accounting_tables.sql');
const pricingSystemItemsSql = read('Sql/store_rpc/12_pricing_system_items.sql');
const systemItemCategoryMigrationSql = read('Sql/store_system_item_category_migration.sql');

assert.doesNotMatch(
    itemPage,
    /groupIndex\s*==\s*0\s*\|\|\s*isInputCategory/,
    '商品管理を初めて開いたときにカテゴリを自動展開しないこと'
);
assert.match(
    itemPage,
    /<details class="item-admin__group" @\(isInputCategory \? "open" : null\)>/,
    '入力エラー時だけ該当カテゴリを展開すること'
);
assert.doesNotMatch(
    itemPageModel,
    /ItemCategoryId\s*=\s*Catalog\.Categories\.FirstOrDefault/,
    '初期表示で追加先カテゴリを選択済みにしないこと'
);
assert.doesNotMatch(schemaSql, /karaoke_categories\s+as\s*\(/i, 'カラオケ専用カテゴリを新規作成しないこと');
assert.match(
    schemaSql,
    /i\.item_type\s*<>\s*'standard'\s*or\s*i\.item_category_id\s+is\s+null/i,
    '既存のシステム商品と未分類商品をシステムカテゴリへ移動すること'
);
assert.match(
    pricingSystemItemsSql,
    /v_system_item_category_id/,
    '会計時に生成する料金用システム商品にもシステムカテゴリを設定すること'
);
assert.match(
    systemItemCategoryMigrationSql,
    /create or replace function store\.ensure_pricing_system_items/i,
    '本番移行時にも料金用システム商品のカテゴリ設定を更新すること'
);

console.log('Item management system-category contract checks passed.');
