import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

const receiptPage = read('Pages/Closing/Receipts.cshtml');

assert.doesNotMatch(
    receiptPage,
    /else\s+if\s*\(Model\.PendingReceiptsLoadStatus\?\.LastUpdatedAt\s+is\s+\{\s*\}\s+lastUpdatedAt\)[\s\S]*?else\s+if\s*\(Model\.CurrentReceipt\s+is\s+null\)/,
    '領収書取得成功時の最終更新表示で、本文フォームまたは空表示の分岐を止めないでください。'
);

assert.match(
    receiptPage,
    /@if\s*\(Model\.PendingReceiptsLoadStatus\?\.LastUpdatedAt\s+is\s+\{\s*\}\s+lastUpdatedAt\)[\s\S]*?@if\s*\(Model\.CurrentReceipt\s+is\s+null\)[\s\S]*?tablet-entry__workspace/,
    '領収書取得成功時は、最終更新と本文領域を同じ成功分岐内で描画してください。'
);
