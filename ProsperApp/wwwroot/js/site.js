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

    window.AppLoading = { show, hide };

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

    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        if (form.dataset.loadingLock === 'false') {
            return;
        }

        if (event.defaultPrevented || !form.checkValidity()) {
            return;
        }

        show(form);
    });

    window.addEventListener('pageshow', () => hide());
})();
