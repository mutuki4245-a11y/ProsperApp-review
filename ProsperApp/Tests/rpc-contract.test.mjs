import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));

const read = (relativePath) =>
    fs.readFileSync(path.join(root, relativePath), 'utf8');

const walk = (relativePath, extension) => {
    const directory = path.join(root, relativePath);
    return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
        const entryPath = path.join(relativePath, entry.name);
        return entry.isDirectory()
            ? walk(entryPath, extension)
            : entry.name.endsWith(extension)
                ? [entryPath]
                : [];
    });
};

const sorted = (values) => [...values].sort();
const difference = (left, right) => sorted([...left].filter((value) => !right.has(value)));

const repositorySource = walk('Infrastructure/Supabase', '.cs')
    .map(read)
    .join('\n');
const csharpRpcNames = new Set(
    [...repositorySource.matchAll(
        /(?:PostRpcArrayAsync|PostRpcArrayResultAsync|PostArrayAsync|PostScalarAsync)\s*\(\s*"(store\.[a-z0-9_]+)"/g
    )].map((match) => match[1])
);

const edgeSource = read('supabase/functions/prosper-rpc/index.ts');
const edgeRpcNames = new Set(
    [...edgeSource.matchAll(/"(store\.[a-z0-9_]+)"\s*,\s*\{/g)]
        .map((match) => match[1])
);

const sqlFiles = [
    'Sql/store_settings_functions.sql',
    ...walk('Sql/store_rpc', '.sql')
];
const sqlSource = sqlFiles.map(read).join('\n');
const sqlDefinitions = [
    ...sqlSource.matchAll(/create\s+or\s+replace\s+function\s+(store\.[a-z0-9_]+)/gi)
].map((match) => match[1].toLowerCase());
const sqlRpcNames = new Set(sqlDefinitions);

assert.deepEqual(
    difference(csharpRpcNames, edgeRpcNames),
    [],
    'C#から呼ぶRPCはprosper-rpc allowlistに必要です。'
);
assert.deepEqual(
    difference(edgeRpcNames, csharpRpcNames),
    [],
    'C#から参照されないRPCをprosper-rpc allowlistへ残さないでください。'
);
assert.deepEqual(
    difference(csharpRpcNames, sqlRpcNames),
    [],
    'C#から呼ぶRPCにはSQL定義が必要です。'
);

const duplicateDefinitions = sorted(
    [...new Set(sqlDefinitions.filter((name, index) => sqlDefinitions.indexOf(name) !== index))]
);
assert.deepEqual(
    duplicateDefinitions,
    [],
    '同名RPCを複数ファイルで上書きしないでください。'
);

for (const rpcName of csharpRpcNames) {
    const escapedName = rpcName.replace('.', '\\.');
    const functionBlock = new RegExp(
        `create\\s+or\\s+replace\\s+function\\s+${escapedName}\\b[\\s\\S]*?\\$\\$;`,
        'i'
    ).exec(sqlSource)?.[0];

    assert.ok(functionBlock, `${rpcName} のSQL定義を読み取れません。`);
    assert.match(functionBlock, /\bsecurity\s+definer\b/i, `${rpcName} はsecurity definerで統一します。`);
    assert.match(
        functionBlock,
        /\bset\s+search_path\s*=\s*(?:pg_catalog\s*,\s*)?public\b/i,
        `${rpcName} はsearch_pathを固定してください。`
    );
}

const grantsSource = read('Sql/store_rpc/99_grants.sql');
assert.match(
    grantsSource,
    /revoke\s+execute\s+on\s+all\s+functions\s+in\s+schema\s+store\s+from\s+public,\s*anon,\s*authenticated,\s*service_role/i,
    'store schemaの全関数から直接実行権限を剥奪してください。'
);
assert.match(
    grantsSource,
    /alter\s+default\s+privileges\s+in\s+schema\s+store\s+revoke\s+execute\s+on\s+functions\s+from\s+public/i,
    '今後追加するstore関数もPUBLIC実行不可を既定にしてください。'
);

const businessDaySource = read('Sql/store_rpc/01_business_day.sql');
assert.match(
    businessDaySource,
    /create\s+or\s+replace\s+function\s+store\.get_business_day_closing_readiness/i,
    '締め条件は構造化RPCへ集約してください。'
);
assert.match(
    businessDaySource,
    /from\s+store\.get_business_day_closing_readiness\s*\(/i,
    '締め実行も画面と同じ準備判定関数を使ってください。'
);
assert.match(
    businessDaySource,
    /if\s+coalesce\(p_ignore_closing_requirements,\s*false\)\s+then[\s\S]*?raise\s+exception\s+'closing_override_disabled'/i,
    '旧締め上書き引数は互換目的に限り、trueを拒否してください。'
);

const operationalSource = read('Sql/store_rpc/14_operational_read_models.sql');
assert.match(
    operationalSource,
    /create\s+or\s+replace\s+function\s+store\.get_business_day_cast_sales_adjustment_overview/i,
    'キャスト売上額調整は一覧・詳細を一括取得してください。'
);
assert.match(
    operationalSource,
    /create\s+or\s+replace\s+function\s+store\.save_business_day_cast_sales_adjustments/i,
    'キャスト売上額調整は営業日単位の一括保存RPCを持ってください。'
);

const checkoutSource = read('Sql/store_rpc/08_checkout_ready.sql');
assert.match(
    checkoutSource,
    /coalesce\(v_payment_method\.requires_received_amount,\s*false\)/i,
    '受取額の要否は決済方法マスタを参照してください。'
);
assert.equal(
    checkoutSource.includes("if v_method_code = 'cash'"),
    false,
    '受取額判定をcash固定へ戻さないでください。'
);
