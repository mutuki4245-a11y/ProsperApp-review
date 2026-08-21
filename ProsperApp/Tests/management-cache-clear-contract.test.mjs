import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';

class StorageFake {
    constructor(entries) {
        this.values = new Map(entries);
    }

    get length() {
        return this.values.size;
    }

    key(index) {
        return [...this.values.keys()][index] ?? null;
    }

    removeItem(key) {
        this.values.delete(key);
    }
}

const source = await readFile(new URL('../wwwroot/js/local-state.js', import.meta.url), 'utf8');
const localStorage = new StorageFake([
    ['prosper:business-home-draft:v2:1', 'draft'],
    ['ProsperApp.LocalSettings', 'settings']
]);
const sessionStorage = new StorageFake([
    ['prosper:attendance-mutation:v2:1', 'pending'],
    ['unrelated-session-value', 'keep']
]);
const document = { addEventListener() {} };
const context = {
    window: { localStorage, sessionStorage },
    document,
    HTMLFormElement: class {}
};

vm.runInNewContext(source, context, { filename: 'local-state.js' });

const result = context.window.ProsperLocalState.clearCacheAndPendingQueues();
assert.equal(result.count, 2);
assert.equal(result.cleared, true);
assert.equal(localStorage.values.has('prosper:business-home-draft:v2:1'), false);
assert.equal(sessionStorage.values.has('prosper:attendance-mutation:v2:1'), false);
assert.equal(localStorage.values.has('ProsperApp.LocalSettings'), true);
assert.equal(sessionStorage.values.has('unrelated-session-value'), true);

const [managementMarkup, managementModel, layout] = await Promise.all([
    readFile(new URL('../Pages/Management/Index.cshtml', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Management/Index.cshtml.cs', import.meta.url), 'utf8'),
    readFile(new URL('../Pages/Shared/_Layout.cshtml', import.meta.url), 'utf8')
]);

assert.match(managementMarkup, /data-clear-app-cache/, 'The cache deletion form clears the device state before posting.');
assert.match(managementMarkup, /data-client-state-cleared/, 'The form reports whether browser storage was cleared.');
assert.match(managementMarkup, /キャッシュと未送信キューをすべて削除/, 'The destructive action names the unsent queue.');
assert.match(managementModel, /OnPostClearCacheAsync\(bool clientStateCleared, CancellationToken ct\)/, 'The response distinguishes browser-storage failures.');
assert.match(layout, /js\/local-state\.js/, 'The local state clearing helper is available on the management page.');

console.log('Management cache clear contract checks passed.');
