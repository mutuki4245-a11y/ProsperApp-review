import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const dataKey = (name) => name.replace(/^data-/, '').replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());

class FakeElement {
    constructor(tagName = 'div') {
        this.tagName = tagName.toUpperCase();
        this.children = [];
        this.dataset = {};
        this.attributes = new Map();
        this.listeners = new Map();
        this.parentElement = null;
        this.hidden = false;
        this.disabled = false;
        this.value = '';
        this.textContent = '';
        this._classes = new Set();
    }

    get className() { return Array.from(this._classes).join(' '); }
    set className(value) { this._classes = new Set(String(value || '').split(/\s+/).filter(Boolean)); }
    get classList() {
        return {
            add: (...names) => names.forEach((name) => this._classes.add(name)),
            remove: (...names) => names.forEach((name) => this._classes.delete(name)),
            contains: (name) => this._classes.has(name),
            toggle: (name, force) => {
                const enabled = force === undefined ? !this._classes.has(name) : Boolean(force);
                if (enabled) this._classes.add(name); else this._classes.delete(name);
                return enabled;
            }
        };
    }

    append(...children) { children.forEach((child) => this.appendChild(child)); }
    appendChild(child) {
        if (child instanceof FakeElement) {
            child.parentElement = this;
            this.children.push(child);
        }
        return child;
    }
    replaceChildren(...children) {
        this.children = [];
        this.textContent = '';
        this.append(...children);
    }
    addEventListener(type, listener) {
        const listeners = this.listeners.get(type) || [];
        listeners.push(listener);
        this.listeners.set(type, listeners);
    }
    dispatch(type, event = { target: this }) { (this.listeners.get(type) || []).forEach((listener) => listener(event)); }
    setAttribute(name, value) {
        this.attributes.set(name, String(value));
        if (name.startsWith('data-')) this.dataset[dataKey(name)] = String(value);
    }
    hasAttribute(name) { return this.attributes.has(name); }
    matches(selector) {
        if (selector.startsWith('#')) return this.attributes.get('id') === selector.slice(1);
        if (selector.startsWith('.')) return selector.slice(1).split('.').every((name) => this._classes.has(name));
        const attribute = selector.match(/^\[([^=\]]+)(?:=["']?([^\]"']+)["']?)?\]$/);
        if (!attribute) return false;
        const [, name, value] = attribute;
        const actual = name.startsWith('data-') ? this.dataset[dataKey(name)] : this.attributes.get(name);
        return actual !== undefined && (value === undefined || actual === value);
    }
    closest(selector) { return this.matches(selector) ? this : this.parentElement?.closest(selector) || null; }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    querySelectorAll(selector) {
        const found = [];
        const visit = (element) => {
            element.children.forEach((child) => {
                if (child.matches(selector)) found.push(child);
                visit(child);
            });
        };
        visit(this);
        return found;
    }
}

const element = () => new FakeElement();
const root = element();
const sourceForm = element();
const modalElement = element();
const paymentModalElement = element();
const paymentRoot = element();
const elements = {
    '[data-business-checkout-message]': element(),
    '[data-business-checkout-table]': element(),
    '[data-business-checkout-issue-panel]': element(),
    '[data-business-checkout-closed-time]': element(),
    '[data-business-checkout-statement]': element(),
    '[data-business-statement-print-panel]': element(),
    '[data-business-statement-print-state]': element(),
    '[data-business-checkout-issue-action]': element(),
    '[data-business-checkout-print-actions]': element(),
    '[data-business-print-statement]': element(),
    '[data-business-release-checkout]': element(),
    '[data-business-proceed-payment]': element(),
    '[data-business-statement-summary]': element(),
    '[data-business-statement-orders]': element(),
    '[data-business-statement-adjustments]': element(),
    '[data-business-statement-totals]': element()
};
root.querySelector = (selector) => elements[selector] || FakeElement.prototype.querySelector.call(root, selector);
const paymentElements = {
    '[data-business-payment-message]': element(),
    '[data-business-payment-table]': element(),
    '[data-business-payment-detail]': element(),
    '[data-business-payment-form]': element(),
    '[data-business-payment-rows]': element(),
    '[data-business-payment-summary]': element(),
    '[data-business-received-row]': element(),
    '[data-business-received-amount]': element(),
    '[data-business-receipt-addressee]': element(),
    '[data-business-confirm-checkout]': element()
};
paymentElements['[data-business-payment-detail]'].appendChild(paymentElements['[data-business-payment-form]']);
paymentRoot.querySelector = (selector) => paymentElements[selector] || FakeElement.prototype.querySelector.call(paymentRoot, selector);
paymentModalElement.querySelector = (selector) => selector === '[data-business-payment-modal]'
    ? paymentRoot
    : FakeElement.prototype.querySelector.call(paymentModalElement, selector);

const documentListeners = new Map();
const document = {
    querySelector: (selector) => ({ '[data-business-checkout-modal]': root, '[data-business-karaoke-form]': sourceForm }[selector] || null),
    getElementById: (id) => ({ businessCheckoutModal: modalElement, businessPaymentModal: paymentModalElement }[id] || null),
    createElement: (tagName) => new FakeElement(tagName),
    addEventListener: (type, listener) => {
        const listeners = documentListeners.get(type) || [];
        listeners.push(listener);
        documentListeners.set(type, listeners);
    },
    dispatch: (type, event) => (documentListeners.get(type) || []).forEach((listener) => listener(event))
};

const modal = {
    shown: 0,
    show() { this.shown += 1; },
    hide() { modalElement.dispatch('hidden.bs.modal'); }
};
const paymentModal = {
    shown: false,
    show() { this.shown = true; },
    hide() { paymentModalElement.dispatch('hidden.bs.modal'); }
};
const storage = new Map();
const requests = [];
const context = {
    console,
    document,
    Intl,
    Date,
    Math,
    Promise,
    JSON,
    localStorage: { getItem: (key) => storage.get(key) || null, setItem: (key, value) => storage.set(key, value), removeItem: (key) => storage.delete(key) },
    fetch: async (url, options = {}) => {
        requests.push({ url, body: options.body ? JSON.parse(options.body) : null });
        return ({
        ok: true,
        json: async () => ({
            succeeded: true,
            printData: { table_display_name: 'A1', total_amount: 4080, opened_at: null, closed_at: null, customer_count: 1, orders: [], adjustments: [] },
            reviewData: { orders: [] }
        })
        });
    },
    window: {
        ProsperBusinessHomeConfig: {
            getCheckoutStatementPrintDataUrl: '/checkout',
            releaseCheckoutReadyUrl: '/release',
            receiptPrinterEnabled: false
        },
        ProsperBusinessHome: {
            getSlip: () => ({ id: 1, status: 'checkout_ready', tableDisplay: 'A1' }),
            buildSlipDetailContent: () => {
                const detail = element();
                const activity = element();
                activity.className = 'business-slip-detail-layout__activity';
                detail.appendChild(activity);
                return detail;
            },
            waitForOperations: async () => true,
            setCheckoutLock: () => {},
            reload: async () => true,
            flush: async () => true
        },
        AppLoading: { show() {}, hide() {} },
        bootstrap: { Modal: { getOrCreateInstance: (target) => target === paymentModalElement ? paymentModal : modal } },
        addEventListener() {},
        dispatchEvent() {},
        alert() {},
        confirm: () => true
    }
};

const source = await readFile(new URL('../wwwroot/js/features/business-checkout.js', import.meta.url), 'utf8');
vm.runInNewContext(source, context, { filename: 'business-checkout.js' });

const settle = async () => {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    await new Promise((resolve) => setTimeout(resolve, 0));
};

const checkoutButton = element();
checkoutButton.dataset.businessStartCheckout = '1';
document.dispatch('click', { target: checkoutButton });
await settle();
elements['[data-business-proceed-payment]'].dispatch('click');
assert.equal(paymentModal.shown, true, '決済操作は専用モーダルを表示すること');
assert.equal(
    paymentElements['[data-business-payment-form]'].parentElement.classList.contains('business-slip-detail-layout__activity'),
    true,
    '決済操作を伝票詳細の左カラムへ配置すること'
);

const paymentRows = paymentElements['[data-business-payment-rows]'];
const cashButton = paymentRows.querySelector('[data-payment-code]');
const cashAmount = paymentRows.querySelector('[data-payment-amount="cash"]');
assert.ok(cashButton, '現金の選択ボタンを描画すること');
assert.ok(cashAmount, '現金の金額欄を描画すること');

paymentRows.dispatch('click', { target: cashButton });
assert.equal(cashAmount.value, 4080, '初回選択時は残額を入力すること');
assert.equal(cashButton.closest('.checkout-payment-row').classList.contains('checkout-payment-row--selected'), true, '選択中だけ行を強調すること');
cashAmount.value = '3000';
paymentElements['[data-business-received-amount]'].value = '5000';
paymentRows.dispatch('click', { target: cashButton });

assert.equal(cashAmount.disabled, true, '選択解除時は金額欄を無効化すること');
assert.equal(cashAmount.value, '', '選択解除時は金額を残さないこと');
assert.equal(paymentElements['[data-business-received-amount]'].value, '', '現金の選択解除時は受取額も残さないこと');
assert.equal(cashButton.closest('.checkout-payment-row').classList.contains('checkout-payment-row--selected'), false, '選択解除時は行の強調も消すこと');

paymentModal.hide();
assert.equal(modal.shown, 2, '決済モーダルを戻ると会計伝票モーダルへ戻ること');

const releaseReadyButton = element();
releaseReadyButton.dataset.businessReleaseCheckoutReady = '1';
document.dispatch('click', { target: releaseReadyButton });
await settle();
assert.equal(
    requests.some((request) => request.url === '/release' && request.body?.slipId === 1),
    true,
    '伝票パネルから会計準備解除を実行すること'
);

console.log('Checkout payment selection reset checks passed.');
