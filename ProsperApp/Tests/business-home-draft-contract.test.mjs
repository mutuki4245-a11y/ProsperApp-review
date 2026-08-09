import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [markup, source, createSlipSource, operationContract, applicationService, flushSql] = await Promise.all([
    readFile(new URL('../Pages/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-home.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/create-slip-modal.js', import.meta.url), 'utf8'),
    readFile(new URL('../Features/BusinessHome/BusinessHomeOperationContract.cs', import.meta.url), 'utf8'),
    readFile(new URL('../Features/BusinessHome/BusinessHomeApplicationService.cs', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/19_current_business_home_flush.sql', import.meta.url), 'utf8')
]);

const operationTypes = [
    'create_slip',
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
assert.match(markup, /initialSnapshot = Model\.InitialSnapshot/, '初回snapshotを営業中トップconfigへ埋め込むこと');

assert.match(source, /const initialSnapshot = config\.initialSnapshot/, '初回描画用snapshotをconfigから読むこと');
assert.match(source, /if \(initialSnapshot && applySnapshot\(initialSnapshot, true\)\) \{[\s\S]*?markDirtyStatus\(\);[\s\S]*?\} else \{[\s\S]*?void loadSlips\(\);[\s\S]*?\}/, '初回は埋め込みsnapshotを優先し、無い時だけ取得すること');
assert.match(source, /setInterval\(\(\) => \{[\s\S]*?void loadSlips\(\);[\s\S]*?\}, refreshIntervalMs\)/, '自動更新はsnapshot取得を継続すること');
assert.match(applicationService, /LoadPageAsync[\s\S]*GetBusinessHomeBootstrapAsync/, '営業中トップ初期ロードはbootstrap RPCを使うこと');
assert.match(source, /prosper:business-home-draft:v2:\$\{config\.departmentId\}/, '営業日前の伝票作成も保持できる店舗単位のlocalStorageキーを使うこと');
assert.match(source, /const restoreBusinessHomeDraft = \(\) => \{[\s\S]*pendingOperations\.set/, '営業中編集操作をlocalStorageから復元すること');
assert.match(source, /karaokeDraft\?\.restore\?\.\(draft\.karaokeLines\)/, '営業中カラオケ下書きをlocalStorageから復元すること');
assert.match(source, /const persistBusinessHomeDraft = \(\) => \{[\s\S]*localStorage\.setItem\(draftStorageKey/, '営業中未送信キューをlocalStorageへ保存すること');
assert.match(source, /pendingFlushBatch/, '通信結果不明時のbatch IDを保持すること');
assert.match(source, /pendingFlushBatch = \{[\s\S]*\.\.\.storedBatch/, '再読込後も同じbatch IDを復元すること');
assert.match(source, /scheduleFlush\(\)/, '復元後は既存flush機構で保存を再開すること');
assert.match(source, /if \(!editorOperationTypes\.has\(operation\?\.operationType\)\)/, '未知の営業中operationTypeをキューへ積まないこと');
assert.match(createSlipSource, /prosper:create-slip-draft:v2:\$\{departmentId\}/, '作成伝票の入力を店舗単位で永続化すること');
assert.match(createSlipSource, /pendingCreateCommand/, '結果不明の作成commandを保持すること');
assert.match(createSlipSource, /sessionStorage|localStorage\.setItem\(draftStorageKey/, '作成伝票の下書きをbrowser storageへ保存すること');
assert.match(createSlipSource, /saveResponse\?\.isRejectedStatus\(result\?\.status\)/, '確定した業務エラーだけ旧operationを破棄すること');
assert.match(createSlipSource, /syncPendingCommandControls/, '結果不明commandの編集中変更を防ぐこと');
assert.match(source, /classifyRow/, '営業中flushは行単位で保存応答を分類すること');
assert.match(source, /pendingFlushBatch[\s\S]*保存結果を確認できません/, '応答不明時はbatch IDを保持して再送すること');
assert.match(flushSql, /jsonb_array_elements\(p_operations\) with ordinality[\s\S]*'succeeded', false/, 'DB拒否時もoperation全行へ拒否結果を返すこと');
assert.match(flushSql, /jsonb_array_elements\(p_karaoke_lines\) with ordinality[\s\S]*'succeeded', false/, 'DB拒否時もkaraoke全行へ拒否結果を返すこと');

for (const operationType of operationTypes) {
    assert.equal(source.includes(`'${operationType}'`), true, `${operationType} をJS契約表に含めること`);
    assert.equal(operationContract.includes(`"${operationType}"`), true, `${operationType} をサーバー側契約に含めること`);
}

console.log('Business home draft contract checks passed.');
