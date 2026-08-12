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

const [program, topPage, topModel, closingPage, closingModel, drinkCostModel, drinkBackModel, castSalesModel, closingDashboardScript, partial, editorScript, attendanceModels, attendanceService] = await Promise.all([
    read('Program.cs'),
    read('Pages/Index.cshtml'),
    read('Pages/Index.cshtml.cs'),
    read('Pages/Closing/Index.cshtml'),
    read('Pages/Closing/Index.cshtml.cs'),
    read('Pages/Closing/DrinkCost.cshtml.cs'),
    read('Pages/Closing/DrinkBacks.cshtml.cs'),
    read('Pages/Closing/CastSalesAdjustment.cshtml.cs'),
    read('wwwroot/js/features/closing-dashboard.js'),
    read('Pages/Shared/_AttendanceEditorModal.cshtml'),
    read('wwwroot/js/features/attendance-editor.js'),
    read('Features/Attendance/AttendanceModels.cs'),
    read('Features/Attendance/AttendanceApplicationService.cs')
]);

assert.equal(await exists('Pages/Attendance.cshtml'), false, '旧 /Attendance Razor page を残さないこと');
assert.equal(await exists('Pages/Attendance.cshtml.cs'), false, '旧 /Attendance PageModel を残さないこと');
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
assert.match(closingPage, /data-drink-delivery-editor[\s\S]*data-editor-url="@Url\.Page\("\/Closing\/DrinkCost", "Editor"\)/, '酒代入力エディタを締め作業内に描画すること');
assert.match(closingPage, /data-cast-sales-editor[\s\S]*data-overview-url="@Url\.Page\("\/Closing\/CastSalesAdjustment", "Overview"\)/, 'キャスト売上額調整エディタを締め作業内に描画すること');
assert.match(closingPage, /data-drink-back-editor[\s\S]*data-editor-url="@Url\.Page\("\/Closing\/DrinkBacks", "Editor"\)/, 'ドリンクバック調整エディタを締め作業内に描画すること');
assert.match(closingPage, /<partial name="_AttendanceEditorModal"/, '締め作業に共有勤怠モーダルを描画すること');
assert.doesNotMatch(closingPage, /\/Closing\/Attendance/, '締め作業から旧勤怠URLへ遷移しないこと');
assert.match(closingModel, /OnGetAttendanceCurrentAsync/, '締め作業PageModelに勤怠current handlerを持つこと');
assert.match(closingModel, /OnPostAttendanceSaveAsync/, '締め作業PageModelに勤怠save handlerを持つこと');
assert.match(drinkCostModel, /RedirectToPage\("\/Closing\/Index", new \{ modal = "drinkDelivery" \}\)/, '旧酒代ページ表示は締め作業モーダルへ戻すこと');
assert.match(drinkBackModel, /RedirectToPage\("\/Closing\/Index", new \{ modal = "drinkBack" \}\)/, '旧ドリンクバックページ表示は締め作業モーダルへ戻すこと');
assert.match(castSalesModel, /RedirectToPage\("\/Closing\/Index", new \{ modal = "castSalesAdjustment" \}\)/, '旧キャスト売上額調整ページ表示は締め作業モーダルへ戻すこと');
assert.match(closingDashboardScript, /const modalTargets = \{[\s\S]*drinkDelivery: '#drinkDeliveryEditorModal'[\s\S]*castSalesAdjustment: '#castSalesAdjustmentShellModal'[\s\S]*drinkBack: '#drinkBackEditorModal'/, '締め作業JSで各調整モーダルを明示起動できること');
assert.match(closingDashboardScript, /new URLSearchParams\(window\.location\.search\)\.get\('modal'\)/, '旧URLから戻ったとき指定モーダルを自動表示すること');

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
