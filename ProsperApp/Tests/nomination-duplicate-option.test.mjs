import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [indexMarkup, createSlipSource, editorSource, slipsStyles, nominationsRpc, businessHomeRpc] = await Promise.all([
    readFile(new URL('../Pages/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/create-slip-modal.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/js/features/business-slip-editor.js', import.meta.url), 'utf8'),
    readFile(new URL('../wwwroot/css/features/slips.css', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/03_slips.sql', import.meta.url), 'utf8'),
    readFile(new URL('../Sql/store_rpc/09_business_home_snapshot.sql', import.meta.url), 'utf8')
]);

assert.equal(indexMarkup.includes('placeholder="お客様名"'), true, '初期のお客様名欄をお客様名と表示すること');
assert.equal(createSlipSource.includes('placeholder="お客様名"'), true, '追加したお客様名欄をお客様名と表示すること');
assert.equal(editorSource.includes('data-business-editor-allow-nomination-duplicates'), true, '指名追加に重複ありのチェックを置くこと');
assert.equal(slipsStyles.includes('.business-slip-editor-action-field--nomination-duplicate'), true, '重複ありのチェックを指名入力の下段に配置すること');
assert.equal(editorSource.includes('availableNominationCasts(allowsDuplicateNominations(form))'), true, '重複ありでは指名済みキャストも候補にすること');
assert.equal(editorSource.includes('!allowsDuplicateNominations(form) && activeNominationCastIds()'), true, '重複ありOFFではクライアント側で重複を拒否すること');
assert.equal(editorSource.includes('allow_duplicate: allowsDuplicateNominations(form)'), true, '重複設定を保存操作へ渡すこと');
assert.equal(nominationsRpc.includes('v_allow_duplicate boolean;'), true, '指名RPCが重複許可フラグを扱うこと');
assert.equal(nominationsRpc.includes('if not v_allow_duplicate and exists'), true, '重複ありOFFではRPCが重複を拒否すること');
assert.equal(businessHomeRpc.includes("'allow_duplicate', coalesce(p_payload->'allow_duplicate', 'false'::jsonb)"), true, '営業中編集RPCが重複設定を指名RPCへ渡すこと');

console.log('Nomination duplicate option checks passed.');
