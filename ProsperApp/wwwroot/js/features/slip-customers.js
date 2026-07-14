(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const showAddCustomerModal = pageData.showAddCustomerModal === true;

    const setSaveStatus = (target, message) => {
        if (!target) {
            return;
        }

        const text = message || '保存済み';
        target.textContent = text;
        if (text.includes('保存中')) {
            target.dataset.saveState = 'saving';
        } else if (text.includes('未保存')) {
            target.dataset.saveState = 'dirty';
        } else if (text.includes('失敗')) {
            target.dataset.saveState = 'error';
        } else {
            target.dataset.saveState = 'saved';
        }
    };

    const setCustomerStatus = (message) => {
        const section = document.getElementById('slipCustomersSection');
        const status = section?.querySelector('[data-partial-status]');
        setSaveStatus(status, message);
    };

    const parseValidation = (root) => {
        if (window.jQuery?.validator?.unobtrusive) {
            window.jQuery.validator.unobtrusive.parse(root);
        }
    };

    const replaceCustomerPartial = async (form) => {
        const section = document.getElementById('slipCustomersSection');
        if (!section) {
            form.submit();
            return;
        }

        setCustomerStatus('保存中');
        window.AppLoading?.show(form);
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });
            if (!response.ok) {
                throw new Error('Customer partial save failed.');
            }

            const html = await response.text();
            section.innerHTML = html;
            parseValidation(section);
            const hasValidationError = section.querySelector('.validation-summary-errors, .field-validation-error');
            setCustomerStatus(hasValidationError ? '保存失敗' : '保存済み');
        } catch (error) {
            setCustomerStatus('保存失敗');
            throw error;
        } finally {
            window.AppLoading?.hide(form);
        }
    };

    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-partial-form="customers"]');
        if (!form) {
            return;
        }

        event.preventDefault();
        replaceCustomerPartial(form).catch(() => {});
    });

    if (showAddCustomerModal) {
        const addCustomerModalElement = document.getElementById('addCustomerModal');
        const addCustomerModal = addCustomerModalElement ? new bootstrap.Modal(addCustomerModalElement) : null;
        addCustomerModal?.show();
    }
})();
