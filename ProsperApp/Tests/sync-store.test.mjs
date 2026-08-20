import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../wwwroot/js/sync-store.js', import.meta.url), 'utf8');
const values = new Map();
const context = {
    window: {
        localStorage: {
            getItem: (key) => values.get(key) ?? null,
            setItem: (key, value) => values.set(key, value),
            removeItem: (key) => values.delete(key)
        },
        fetch: () => {
            throw new Error('fetch is not used by this test');
        }
    }
};

vm.runInNewContext(source, context, { filename: 'sync-store.js' });

const store = context.window.ProsperSync.getStore('same-revision-contract', { persist: false });
let notifications = 0;
store.subscribe(() => { notifications += 1; });

assert.equal(store.replace({ value: 'first' }, 12), true, '初回revisionは受理すること');
assert.equal(store.replace({ value: 'duplicate' }, 12), false, '同一の数値revisionは拒否すること');
assert.equal(store.get().payload.value, 'first', '同一revisionのpayloadで状態を上書きしないこと');
assert.equal(notifications, 1, '拒否した同一revisionでは購読者へ通知しないこと');
assert.equal(store.replace({ value: 'newer' }, 13), true, '新しいrevisionは受理すること');

const textStore = context.window.ProsperSync.getStore('same-text-revision-contract', { persist: false });
assert.equal(textStore.replace({ value: 1 }, 'revision-a'), true);
assert.equal(textStore.replace({ value: 2 }, 'revision-a'), false, '同一の文字列revisionも拒否すること');

console.log('Sync store revision checks passed.');
