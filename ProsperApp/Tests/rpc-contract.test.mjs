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
        /(?:PostRpcArrayAsync|PostArrayAsync|PostScalarAsync)\s*\(\s*"(store\.[a-z0-9_]+)"/g
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
        /\bset\s+search_path\s*=\s*public\b/i,
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
