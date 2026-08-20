import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');
const [layout, validation, index, site, createSlip, management, receiptQueue] = await Promise.all([
    read('Pages/Shared/_Layout.cshtml'),
    read('Pages/Shared/_ValidationScriptsPartial.cshtml'),
    read('Pages/Index.cshtml'),
    read('wwwroot/js/site.js'),
    read('wwwroot/js/features/create-slip-modal.js'),
    read('wwwroot/js/features/management-editor.js'),
    read('wwwroot/js/features/receipt-work-queue.js')
]);

assert.doesNotMatch(layout, /<script\s+type="importmap"><\/script>/, '空のimportmapを出力しないこと');
assert.doesNotMatch(layout, /lib\/jquery\/dist\/jquery\.min\.js/, 'jQueryを全ページで読み込まないこと');
assert.match(validation, /jquery\.min\.js[\s\S]*jquery\.validate\.min\.js[\s\S]*jquery\.validate\.unobtrusive\.min\.js/, '検証partial内でjQueryを検証ライブラリより先に読み込むこと');

assert.match(site, /window\.ProsperHtml\s*=\s*Object\.freeze/);
assert.match(site, /escape:\s*escapeHtml/);
for (const consumer of [createSlip, management, receiptQueue]) {
    assert.match(consumer, /const escapeHtml = window\.ProsperHtml\.escape;/, '共通HTML escapeを利用すること');
    assert.doesNotMatch(consumer, /replaceAll\('&', '&amp;'\)/, 'HTML escape実装を複製しないこと');
}

assert.match(index, /document\.addEventListener\('load', onLoad, true\)/, 'SII SDKのloadはJS listenerで監視すること');
assert.match(index, /document\.addEventListener\('error', onError, true\)/, 'SII SDKのerrorはJS listenerで監視すること');
assert.doesNotMatch(index, /\son(?:load|error)=/, 'インラインイベントハンドラーを出力しないこと');

console.log('UI cleanup contract checks passed.');
