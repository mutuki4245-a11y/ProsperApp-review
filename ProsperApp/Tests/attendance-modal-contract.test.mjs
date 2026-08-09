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

const [program, topPage, topModel, closingPage, closingModel, partial] = await Promise.all([
    read('Program.cs'),
    read('Pages/Index.cshtml'),
    read('Pages/Index.cshtml.cs'),
    read('Pages/Closing/Index.cshtml'),
    read('Pages/Closing/Index.cshtml.cs'),
    read('Pages/Shared/_AttendanceEditorModal.cshtml')
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
assert.match(closingPage, /<partial name="_AttendanceEditorModal"/, '締め作業に共有勤怠モーダルを描画すること');
assert.doesNotMatch(closingPage, /\/Closing\/Attendance/, '締め作業から旧勤怠URLへ遷移しないこと');
assert.match(closingModel, /OnGetAttendanceCurrentAsync/, '締め作業PageModelに勤怠current handlerを持つこと');
assert.match(closingModel, /OnPostAttendanceSaveAsync/, '締め作業PageModelに勤怠save handlerを持つこと');

assert.match(partial, /id="attendanceEditorModal"/, '共有勤怠モーダルのroot IDを持つこと');
assert.match(partial, /window\.prosperAttendanceEditor/, '共有部分ビューで勤怠エディタ設定を出力すること');
assert.match(partial, /currentUrl = Model\.CurrentUrl/, 'current handler URLを呼び出し元から渡すこと');
assert.match(partial, /saveUrl = Model\.SaveUrl/, 'save handler URLを呼び出し元から渡すこと');

console.log('Attendance modal contract checks passed.');
