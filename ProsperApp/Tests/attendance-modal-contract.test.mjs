import assert from 'node:assert/strict';
import { access, readFile } from 'node:fs/promises';

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');
const exists = async (path) => {
    try {
        await access(new URL(`../${path}`, import.meta.url));
        return true;
    } catch {
        return false;
    }
};

const [program, topPage, topModel, closingPage, closingModel, drinkDeliveryHandlers, drinkBackHandlers, castSalesHandlers, closingDashboardScript, closingCss, partial, editorScript, attendanceModels, attendanceService] = await Promise.all([
    read('Program.cs'),
    read('Pages/Index.cshtml'),
    read('Pages/Index.cshtml.cs'),
    read('Pages/Closing/Index.cshtml'),
    read('Pages/Closing/Index.cshtml.cs'),
    read('Pages/Closing/Index.DrinkDelivery.cs'),
    read('Pages/Closing/Index.DrinkBack.cs'),
    read('Pages/Closing/Index.CastSalesAdjustment.cs'),
    read('wwwroot/js/features/closing-dashboard.js'),
    read('wwwroot/css/features/closing.css'),
    read('Pages/Shared/_AttendanceEditorModal.cshtml'),
    read('wwwroot/js/features/attendance-editor.js'),
    read('Features/Attendance/AttendanceModels.cs'),
    read('Features/Attendance/AttendanceApplicationService.cs')
]);

assert.equal(await exists('Pages/Attendance.cshtml'), false, '旧 /Attendance Razor page を残さないこと');
assert.equal(await exists('Pages/Attendance.cshtml.cs'), false, '旧 /Attendance PageModel を残さないこと');
for (const oldPage of ['DrinkCost', 'DrinkBacks', 'CastSalesAdjustment']) {
    assert.equal(await exists(`Pages/Closing/${oldPage}.cshtml`), false, `旧 /Closing/${oldPage} Razor page を残さないこと`);
    assert.equal(await exists(`Pages/Closing/${oldPage}.cshtml.cs`), false, `旧 /Closing/${oldPage} PageModel を残さないこと`);
}
assert.doesNotMatch(program, /AddPageRoute\("\/Attendance"/, '旧 /Closing/Attendance 追加ルートを残さないこと');
assert.match(program, /AddRazorPages\(\)/, 'Razor Pages は追加ルートなしで登録すること');

assert.match(topPage, /data-bs-target="#attendanceEditorModal"/, 'トップから勤怠モーダルを開けること');
assert.match(topPage, /<partial name="_AttendanceEditorModal"/, 'トップに共有勤怠モーダルを描画すること');
assert.match(topPage, /js\/features\/attendance-editor\.js/, 'トップで勤怠エディタJSを読み込むこと');
assert.match(topModel, /OnGetAttendanceCurrentAsync/, 'トップPageModelに勤怠current handlerを持つこと');
assert.match(topModel, /OnPostAttendanceSaveAsync/, 'トップPageModelに勤怠save handlerを持つこと');

assert.match(closingPage, /data-closing-panel="attendance"[\s\S]*data-bs-target="#attendanceEditorModal"/, '締め作業から勤怠モーダルを開けること');
assert.match(closingPage, /data-closing-panel="drinkDelivery"[\s\S]*data-bs-target="#drinkDeliveryEditorModal"/, '締め作業から酒代入力モーダルを開けること');
assert.match(closingPage, /data-closing-panel="castSalesAdjustment"[\s\S]*data-bs-target="#castSalesAdjustmentShellModal"/, '締め作業からキャスト売上額調整モーダルを開けること');
assert.match(closingPage, /data-closing-panel="drinkBack"[\s\S]*data-bs-target="#drinkBackEditorModal"/, '締め作業からドリンクバック調整モーダルを開けること');
assert.match(closingPage, /data-drink-delivery-editor[\s\S]*data-editor-url="@Url\.Page\("\/Closing\/Index", "DrinkDeliveryEditor"\)/, '酒代入力エディタを締め作業内に描画すること');
assert.match(closingPage, /data-cast-sales-editor[\s\S]*data-overview-url="@Url\.Page\("\/Closing\/Index", "CastSalesOverview"\)/, 'キャスト売上額調整エディタを締め作業内に描画すること');
assert.match(closingPage, /data-drink-back-editor[\s\S]*data-editor-url="@Url\.Page\("\/Closing\/Index", "DrinkBackEditor"\)/, 'ドリンクバック調整エディタを締め作業内に描画すること');
assert.match(closingPage, /<partial name="_AttendanceEditorModal"/, '締め作業に共有勤怠モーダルを描画すること');
assert.doesNotMatch(closingPage, /\/Closing\/Attendance/, '締め作業から旧勤怠URLへ遷移しないこと');
assert.match(closingModel, /OnGetAttendanceCurrentAsync/, '締め作業PageModelに勤怠current handlerを持つこと');
assert.match(closingModel, /OnPostAttendanceSaveAsync/, '締め作業PageModelに勤怠save handlerを持つこと');
assert.match(drinkDeliveryHandlers, /OnGetDrinkDeliveryEditorAsync/, '締め作業PageModelに酒代取得handlerを持つこと');
assert.match(drinkDeliveryHandlers, /OnPostDrinkDeliverySaveV2Async/, '締め作業PageModelに酒代保存handlerを持つこと');
assert.match(drinkBackHandlers, /OnGetDrinkBackEditorAsync/, '締め作業PageModelにドリンクバック取得handlerを持つこと');
assert.match(drinkBackHandlers, /OnPostDrinkBackSaveAsync/, '締め作業PageModelにドリンクバック保存handlerを持つこと');
assert.match(castSalesHandlers, /OnGetCastSalesOverviewAsync/, '締め作業PageModelにキャスト売上取得handlerを持つこと');
assert.match(castSalesHandlers, /OnPostCastSalesSaveV2Async/, '締め作業PageModelにキャスト売上保存handlerを持つこと');
assert.match(closingDashboardScript, /const modalTargets = \{[\s\S]*drinkDelivery: '#drinkDeliveryEditorModal'[\s\S]*castSalesAdjustment: '#castSalesAdjustmentShellModal'[\s\S]*drinkBack: '#drinkBackEditorModal'/, '締め作業JSで各調整モーダルを明示起動できること');
assert.doesNotMatch(closingDashboardScript, /URLSearchParams[\s\S]*get\('modal'\)/, '旧ページリダイレクト用のモーダル指定を残さないこと');
assert.match(closingCss, /\.closing-attendance-table\s*\{[\s\S]*gap:\s*0\.25rem;/, '勤怠行間を詰めて一覧性を確保すること');
assert.match(closingCss, /\.closing-attendance-table__row\s*\{[\s\S]*min-height:\s*3\.25rem;[\s\S]*padding:\s*0\.35rem 0\.5rem;/, '勤怠行をコンパクトにすること');
assert.match(closingCss, /@media \(min-width:\s*768px\)[\s\S]*\.closing-attendance-table__field > span:first-child\s*\{[\s\S]*display:\s*none;/, 'デスクトップでは表見出しと重複する行内ラベルを隠すこと');

assert.match(partial, /id="attendanceEditorModal"/, '共有勤怠モーダルのroot IDを持つこと');
assert.match(partial, /window\.prosperAttendanceEditor/, '共有部分ビューで勤怠エディタ設定を出力すること');
assert.match(partial, /currentUrl = Model\.CurrentUrl/, 'current handler URLを呼び出し元から渡すこと');
assert.match(partial, /saveUrl = Model\.SaveUrl/, 'save handler URLを呼び出し元から渡すこと');

assert.match(editorScript, /const ensureOption = \(select, value, label = value\) =>/, '保存済み時刻が現在の候補外でもselectへ復元できること');
assert.match(editorScript, /setSelectValue\(clockIn\(row\), entry\.clockInTime, config\.defaultClockInTime\)/, '保存済み出勤時刻を候補へ追加してから選択すること');
assert.match(editorScript, /setSelectValue\(clockOut\(row\), entry\.clockOutTime, config\.defaultClockOutTime\)/, '保存済み退勤時刻を候補へ追加してから選択すること');
assert.doesNotMatch(editorScript, /clockIn\(row\)\.value = entry\.clockInTime \|\| config\.defaultClockInTime \|\| '';/, '保存済み出勤時刻を直接代入して候補外時刻を空にしないこと');
assert.doesNotMatch(editorScript, /clockOut\(row\)\.value = entry\.clockOutTime \|\| config\.defaultClockOutTime \|\| '';/, '保存済み退勤時刻を直接代入して候補外時刻を空にしないこと');
assert.match(attendanceModels, /public DateOnly\? BusinessDate \{ get; set; \}/, '勤怠保存payloadに表示中営業日の日付を持てること');
assert.match(editorScript, /let businessDayDate = null;/, '勤怠エディタが表示中営業日の日付を保持すること');
assert.match(editorScript, /businessDayDate = snapshot\.hasBusinessDay[\s\S]*snapshot\.businessDay\?\.businessDate[\s\S]*: null;/, '再取得したopen営業日の日付を保存payloadへ使うこと');
assert.match(editorScript, /businessDate: businessDayDate,/, '勤怠保存payloadにopen営業日の日付を含めること');
assert.match(editorScript, /pendingCommand\.businessDate = businessDayDate;/, '既存の保留中保存コマンドにもopen営業日の日付を補完すること');
assert.match(attendanceService, /var businessDate = input\.BusinessDate is \{ \} submittedBusinessDate && submittedBusinessDate != default[\s\S]*\? submittedBusinessDate[\s\S]*: _storeClock\.GetCurrentBusinessDate\(\);/, 'サーバー保存はpayloadのopen営業日を優先し、未指定時だけ現在営業日へフォールバックすること');
assert.match(attendanceService, /input\.BusinessDayRevision,[\s\S]*businessDate,[\s\S]*castEntries,/, 'DB保存へ渡す営業日の日付がサーバー現在日付固定ではないこと');

console.log('Attendance modal contract checks passed.');
