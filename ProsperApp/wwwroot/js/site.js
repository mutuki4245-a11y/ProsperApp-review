// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    let isLocked = false;
    let modalLockCount = 0;
    let modalScrollY = 0;
    let modalPreviousBodyStyle = null;
    let modalPreviousHtmlOverflow = '';

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

    const lockPageScrollForModal = () => {
        modalLockCount += 1;
        if (modalLockCount > 1) {
            return;
        }

        modalScrollY = window.scrollY || document.documentElement.scrollTop || 0;
        modalPreviousBodyStyle = {
            position: document.body.style.position,
            top: document.body.style.top,
            left: document.body.style.left,
            right: document.body.style.right,
            width: document.body.style.width,
            overflow: document.body.style.overflow
        };
        modalPreviousHtmlOverflow = document.documentElement.style.overflow;
        document.documentElement.style.overflow = 'hidden';
        document.body.style.position = 'fixed';
        document.body.style.top = `-${modalScrollY}px`;
        document.body.style.left = '0';
        document.body.style.right = '0';
        document.body.style.width = '100%';
        document.body.style.overflow = 'hidden';
    };

    const unlockPageScrollForModal = () => {
        modalLockCount = Math.max(0, modalLockCount - 1);
        if (modalLockCount > 0 || document.querySelector('.modal.show')) {
            return;
        }

        if (modalPreviousBodyStyle) {
            document.body.style.position = modalPreviousBodyStyle.position;
            document.body.style.top = modalPreviousBodyStyle.top;
            document.body.style.left = modalPreviousBodyStyle.left;
            document.body.style.right = modalPreviousBodyStyle.right;
            document.body.style.width = modalPreviousBodyStyle.width;
            document.body.style.overflow = modalPreviousBodyStyle.overflow;
        }
        document.documentElement.style.overflow = modalPreviousHtmlOverflow;
        window.scrollTo(0, modalScrollY);
        modalPreviousBodyStyle = null;
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

    window.AppLoading = { show, hide };
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
    document.addEventListener('show.bs.modal', lockPageScrollForModal);
    document.addEventListener('hidden.bs.modal', unlockPageScrollForModal);
})();
