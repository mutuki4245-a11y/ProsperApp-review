import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [markup, source, operationContract] = await Promise.all([
    readFile(new URL('../Pages/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-home.js', import.meta.url), 'utf8'),
    readFile(new URL('../Features/BusinessHome/BusinessHomeOperationContract.cs', import.meta.url), 'utf8')
]);

const operationTypes = [
    'add_customer',
    'update_customer',
    'leave_customer',
    'add_nomination',
    'cancel_nomination',
    'add_adjustment',
    'void_adjustment',
    'add_order',
    'void_order'
];

assert.match(markup, /data-business-estimated-sales-amount>@\(Model\.HasCurrentBusinessDay \? "取得中" : StoreUiText\.Yen\(0\)\)/, '見込み売上は初期0円ではなく取得中を表示すること');
assert.match(markup, /departmentId = Model\.StoreContext\?\.DepartmentId/, '営業中下書きキーに店舗IDを含めること');
assert.match(markup, /businessDayId = Model\.CurrentBusinessDayId/, '営業中下書きキーに営業日IDを含めること');

assert.match(source, /prosper:business-home-draft:v1:\$\{config\.departmentId\}:\$\{config\.businessDayId\}/, '営業中下書きは店舗・営業日単位のlocalStorageキーを使うこと');
assert.match(source, /const restoreBusinessHomeDraft = \(\) => \{[\s\S]*pendingOperations\.set/, '営業中編集操作をlocalStorageから復元すること');
assert.match(source, /karaokeDraft\?\.restore\?\.\(draft\.karaokeLines\)/, '営業中カラオケ下書きをlocalStorageから復元すること');
assert.match(source, /const persistBusinessHomeDraft = \(\) => \{[\s\S]*localStorage\.setItem\(draftStorageKey/, '営業中未送信キューをlocalStorageへ保存すること');
assert.match(source, /scheduleFlush\(\)/, '復元後は既存flush機構で保存を再開すること');
assert.match(source, /if \(!editorOperationTypes\.has\(operation\?\.operationType\)\)/, '未知の営業中operationTypeをキューへ積まないこと');

for (const operationType of operationTypes) {
    assert.equal(source.includes(`'${operationType}'`), true, `${operationType} をJS契約表に含めること`);
    assert.equal(operationContract.includes(`"${operationType}"`), true, `${operationType} をサーバー側契約に含めること`);
}

console.log('Business home draft contract checks passed.');
