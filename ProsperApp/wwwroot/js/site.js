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

    window.AppLoading = { show, hide };

    document.addEventListener('click', (event) => {
        const submitter = event.target.closest('button[type="submit"], input[type="submit"]');
        if (!submitter || !submitter.form) {
            return;
        }

        submitter.form.dataset.appLoadingSubmitter = ensureSubmitterId(submitter);
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
