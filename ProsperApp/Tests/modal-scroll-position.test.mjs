import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const documentListeners = new Map();
const bodyClasses = new Set();
const body = {
    classList: {
        add: (...names) => names.forEach((name) => bodyClasses.add(name)),
        remove: (...names) => names.forEach((name) => bodyClasses.delete(name))
    },
    style: {}
};
const documentElement = { scrollTop: 640, style: {} };
const document = {
    body,
    documentElement,
    getElementById: () => null,
    querySelector: () => null,
    querySelectorAll: () => [],
    createElement: () => ({
        innerHTML: '',
        querySelector: () => null,
        querySelectorAll: () => []
    }),
    addEventListener: (type, listener) => {
        const listeners = documentListeners.get(type) || [];
        listeners.push(listener);
        documentListeners.set(type, listeners);
    },
    dispatch: (type) => {
        const event = { target: document };
        (documentListeners.get(type) || []).forEach((listener) => listener(event));
    }
};

const scrollToCalls = [];
const window = {
    location: new URL('https://example.test/'),
    scrollY: 640,
    scrollTo: (...args) => scrollToCalls.push(args),
    addEventListener() {},
    queueMicrotask,
    setTimeout,
    clearTimeout
};

class FakeElement {}
class FakeInputElement extends FakeElement {}
class FakeFormElement extends FakeElement {}

const context = {
    console,
    document,
    window,
    URL,
    FormData,
    Element: FakeElement,
    HTMLInputElement: FakeInputElement,
    HTMLFormElement: FakeFormElement
};

const source = await readFile(new URL('../wwwroot/js/site.js', import.meta.url), 'utf8');
vm.runInNewContext(source, context, { filename: 'site.js' });

document.dispatch('show.bs.modal');
document.dispatch('hidden.bs.modal');

assert.deepEqual(scrollToCalls, [], 'モーダルを閉じてもページを再スクロールしないこと');
assert.equal(body.style.position || '', '', 'モーダル表示時にページ本文をfixed化しないこと');

console.log('Modal scroll position checks passed.');
