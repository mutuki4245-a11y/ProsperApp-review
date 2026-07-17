(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const showAddCustomerModal = pageData.showAddCustomerModal === true;

    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-partial-form="customers"]');
        if (!form) {
            return;
        }

        event.preventDefault();
        window.PartialForms?.submit(form, {
            section: 'slipCustomersSection',
            modalId: form.closest('#addCustomerModal') ? 'addCustomerModal' : null
        });
    });

    if (showAddCustomerModal) {
        const addCustomerModalElement = document.getElementById('addCustomerModal');
        const addCustomerModal = addCustomerModalElement ? new bootstrap.Modal(addCustomerModalElement) : null;
        addCustomerModal?.show();
    }
})();
