// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    let isLocked = false;

    const overlay = () => document.getElementById('appLoadingOverlay');

    const setSubmitButtonsDisabled = (form, disabled) => {
        if (!form) {
            return;
        }

        const submitter = form.dataset.appLoadingSubmitter
            ? document.getElementById(form.dataset.appLoadingSubmitter)
            : null;
        const buttons = submitter
            ? [submitter]
            : Array.from(form.querySelectorAll('button[type="submit"], input[type="submit"]'));

        buttons.forEach((button) => {
            if (disabled) {
                button.dataset.appLoadingPreviousDisabled = button.disabled ? 'true' : 'false';
                button.disabled = true;
            } else if (button.dataset.appLoadingPreviousDisabled === 'false') {
                button.disabled = false;
            }
        });
    };

    const show = (form = null) => {
        if (isLocked) {
            return;
        }

        isLocked = true;
        document.body.classList.add('is-app-loading');
        overlay()?.classList.add('is-visible');
        overlay()?.setAttribute('aria-hidden', 'false');
        setSubmitButtonsDisabled(form, true);
    };

    const hide = (form = null) => {
        isLocked = false;
        document.body.classList.remove('is-app-loading');
        overlay()?.classList.remove('is-visible');
        overlay()?.setAttribute('aria-hidden', 'true');
        setSubmitButtonsDisabled(form, false);
    };

    const ensureSubmitterId = (submitter) => {
        if (submitter.id) {
            return submitter.id;
        }

        const suffix = window.crypto?.randomUUID ? window.crypto.randomUUID() : Date.now().toString(36);
        submitter.id = `app-submit-${suffix}`;
        return submitter.id;
    };

    const closest = (target, selector) => target instanceof Element ? target.closest(selector) : null;

    const shouldPreventModalEnterSubmit = (target, event) => {
        if (event.key !== 'Enter' || event.isComposing || !(target instanceof HTMLInputElement)) {
            return false;
        }

        if (['button', 'submit', 'reset', 'checkbox', 'radio', 'file', 'image'].includes(target.type)) {
            return false;
        }

        return target.form?.closest('.modal') !== null;
    };

    const isLoadingDisabled = (element) => element?.closest('[data-loading-lock="false"]') !== null;

    const deferAfterClickHandlers = (callback) => {
        if (typeof window.queueMicrotask === 'function') {
            window.queueMicrotask(callback);
            return;
        }

        window.setTimeout(callback, 0);
    };

    const shouldLockForNavigation = (anchor, event) => {
        if (
            isLocked ||
            event.defaultPrevented ||
            event.button !== 0 ||
            event.altKey ||
            event.ctrlKey ||
            event.metaKey ||
            event.shiftKey ||
            isLoadingDisabled(anchor) ||
            anchor.hasAttribute('download')
        ) {
            return false;
        }

        const target = anchor.getAttribute('target');
        if (target && target.toLowerCase() !== '_self') {
            return false;
        }

        const rawHref = anchor.getAttribute('href')?.trim();
        if (!rawHref || rawHref === '#') {
            return false;
        }

        const lowerHref = rawHref.toLowerCase();
        if (
            lowerHref.startsWith('javascript:') ||
            lowerHref.startsWith('mailto:') ||
            lowerHref.startsWith('tel:')
        ) {
            return false;
        }

        const url = new URL(anchor.href, window.location.href);
        const isSamePageHash =
            url.origin === window.location.origin &&
            url.pathname === window.location.pathname &&
            url.search === window.location.search &&
            url.hash;

        return !isSamePageHash;
    };

    const configureStaticModals = () => {
        document.querySelectorAll('.modal').forEach((modal) => {
            if (!modal.hasAttribute('data-bs-backdrop')) {
                modal.setAttribute('data-bs-backdrop', 'static');
            }

            if (!modal.hasAttribute('data-bs-keyboard')) {
                modal.setAttribute('data-bs-keyboard', 'false');
            }
        });
    };

    const terminalSaveStatusMessages = {
        saved: '保存済み',
        dirty: '未保存',
        saving: '保存中',
        error: '保存失敗'
    };

    const terminalSaveStatusStates = new Set(Object.keys(terminalSaveStatusMessages));

    const setTerminalSaveStatus = (target, state, message) => {
        if (!target) {
            return;
        }

        const normalizedState = terminalSaveStatusStates.has(state) ? state : 'saved';
        target.textContent = message || terminalSaveStatusMessages[normalizedState];
        target.dataset.saveState = normalizedState;
        if (target.hasAttribute('data-hide-when-saved')) {
            target.hidden = normalizedState === 'saved';
        }
    };

    const formatMoneyAmount = (value) => Math.round(Number(value) || 0).toLocaleString('ja-JP');
    const formatMoneyYen = (value) => `${formatMoneyAmount(value)} 円`;
    const hasValidationErrors = (root) =>
        root.querySelector('.validation-summary-errors, .field-validation-error, .input-validation-error') !== null;
    const parseValidation = (root) => {
        if (window.jQuery?.validator?.unobtrusive) {
            window.jQuery.validator.unobtrusive.parse(root);
        }
    };
    const hideModalForReplace = (modalElement) => new Promise((resolve) => {
        if (!modalElement || !modalElement.classList.contains('show') || !window.bootstrap?.Modal) {
            resolve();
            return;
        }

        const instance = bootstrap.Modal.getOrCreateInstance(modalElement);
        const timeout = window.setTimeout(resolve, 400);
        modalElement.addEventListener('hidden.bs.modal', () => {
            window.clearTimeout(timeout);
            resolve();
        }, { once: true });
        instance.hide();
    });
    const submitPartialForm = async (form, options) => {
        const section = typeof options.section === 'string'
            ? document.getElementById(options.section)
            : options.section;
        if (!section) {
            form.submit();
            return false;
        }

        if (form.dataset.partialSubmitting === 'true') {
            return false;
        }

        form.dataset.partialSubmitting = 'true';
        const modalElement = options.modalId ? document.getElementById(options.modalId) : form.closest('.modal');
        const status = options.status ? section.querySelector(options.status) : null;
        setTerminalSaveStatus(status, 'saving');
        show(form);
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });
            if (!response.ok) {
                throw new Error('Partial form request failed.');
            }

            const html = await response.text();
            const preview = document.createElement('div');
            preview.innerHTML = html;
            const hasErrors = hasValidationErrors(preview);
            await hideModalForReplace(modalElement);
            section.innerHTML = html;
            configureStaticModals();
            parseValidation(section);
            setTerminalSaveStatus(options.status ? section.querySelector(options.status) : status, hasErrors ? 'error' : 'saved');
            options.afterReplace?.(section, { hasErrors });
            if (hasErrors && options.modalId && window.bootstrap?.Modal) {
                bootstrap.Modal.getOrCreateInstance(document.getElementById(options.modalId))?.show();
            }

            return true;
        } catch {
            setTerminalSaveStatus(status, 'error');
            return false;
        } finally {
            delete form.dataset.partialSubmitting;
            hide(form);
        }
    };

    const showAppDialog = ({ message, title = '確認', confirm = false, confirmText = 'OK', cancelText = 'キャンセル' }) => {
        if (!window.bootstrap?.Modal) {
            return Promise.resolve(confirm ? window.confirm(message) : (window.alert(message), true));
        }

        return new Promise((resolve) => {
            const modalElement = document.createElement('div');
            modalElement.className = 'modal fade';
            modalElement.tabIndex = -1;
            modalElement.setAttribute('aria-hidden', 'true');
            modalElement.setAttribute('data-bs-backdrop', 'static');
            modalElement.setAttribute('data-bs-keyboard', 'false');
            modalElement.innerHTML = `
                <div class="modal-dialog">
                    <div class="modal-content">
                        <div class="modal-header">
                            <h2 class="modal-title fs-5"></h2>
                            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="閉じる"></button>
                        </div>
                        <div class="modal-body">
                            <p class="mb-0" data-app-dialog-message></p>
                        </div>
                        <div class="modal-footer">
                            ${confirm ? '<button class="btn btn-outline-secondary" type="button" data-app-dialog-cancel></button>' : ''}
                            <button class="btn btn-primary" type="button" data-app-dialog-ok></button>
                        </div>
                    </div>
                </div>`;

            modalElement.querySelector('.modal-title').textContent = title;
            modalElement.querySelector('[data-app-dialog-message]').textContent = message;
            modalElement.querySelector('[data-app-dialog-ok]').textContent = confirmText;
            modalElement.querySelector('[data-app-dialog-cancel]')?.replaceChildren(cancelText);

            document.body.appendChild(modalElement);
            const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
            let settled = false;
            const finish = (value) => {
                settled = true;
                resolve(value);
                modal.hide();
            };
            modalElement.querySelector('[data-app-dialog-ok]').addEventListener('click', () => finish(true), { once: true });
            modalElement.querySelector('[data-app-dialog-cancel]')?.addEventListener('click', () => finish(false), { once: true });
            modalElement.addEventListener('hidden.bs.modal', () => {
                modal.dispose?.();
                modalElement.remove();
                if (!settled) {
                    resolve(false);
                }
            }, { once: true });
            modal.show();
        });
    };

    window.AppLoading = { show, hide };
    window.AppConfirm = {
        alert: (message, title = '確認') => showAppDialog({ message, title, confirm: false }),
        confirm: (message, title = '確認') => showAppDialog({ message, title, confirm: true, confirmText: '実行', cancelText: '戻る' })
    };
    window.MoneyText = {
        amount: formatMoneyAmount,
        yen: formatMoneyYen
    };
    window.TerminalSaveStatus = {
        set: setTerminalSaveStatus,
        saved: (target, message) => setTerminalSaveStatus(target, 'saved', message),
        dirty: (target, message) => setTerminalSaveStatus(target, 'dirty', message),
        saving: (target, message) => setTerminalSaveStatus(target, 'saving', message),
        error: (target, message) => setTerminalSaveStatus(target, 'error', message)
    };
    window.PartialForms = {
        submit: submitPartialForm,
        prepareDynamicContent: (root = document) => {
            configureStaticModals();
            parseValidation(root);
        }
    };

    document.addEventListener('click', (event) => {
        const submitter = closest(event.target, 'button[type="submit"], input[type="submit"]');
        if (submitter?.form) {
            submitter.form.dataset.appLoadingSubmitter = ensureSubmitterId(submitter);
        }

        const anchor = closest(event.target, 'a[href]');
        if (!anchor || !shouldLockForNavigation(anchor, event)) {
            return;
        }

        deferAfterClickHandlers(() => {
            if (!event.defaultPrevented) {
                show();
            }
        });
    });

    document.addEventListener('keydown', (event) => {
        if (shouldPreventModalEnterSubmit(event.target, event)) {
            event.preventDefault();
        }
    });

    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        if (form.dataset.loadingLock === 'false') {
            return;
        }

        if (isLocked) {
            event.preventDefault();
            return;
        }

        if (event.defaultPrevented || !form.checkValidity()) {
            return;
        }

        show(form);
    });

    window.addEventListener('pageshow', () => hide());
    configureStaticModals();
})();
