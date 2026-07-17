(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    let showAddNominationModal = pageData.showAddNominationModal === true;
    const companionNominationPrice = '3000';

    const syncCompanionDefaultPrice = (kindSelect, force = false) => {
        const selectedOption = kindSelect?.selectedOptions?.[0];
        if (!selectedOption || selectedOption.dataset.companion !== 'true') {
            return;
        }

        const form = kindSelect.closest('form');
        const priceSelect = form?.querySelector('.nomination-row__price');
        if (!priceSelect || !Array.from(priceSelect.options).some((option) => option.value === companionNominationPrice)) {
            return;
        }

        if (force || priceSelect.value === '' || priceSelect.value === '1000') {
            priceSelect.value = companionNominationPrice;
        }
    };

    const syncCurrentDefaults = () => {
        document.querySelectorAll('#addNominationModal .nomination-row__kind').forEach((kindSelect) => {
            syncCompanionDefaultPrice(kindSelect);
        });
    };

    const showModal = () => {
        const modalElement = document.getElementById('addNominationModal');
        const modal = modalElement ? new bootstrap.Modal(modalElement) : null;
        modal?.show();
    };

    const section = document.getElementById('slipNominationsSection');
    section?.addEventListener('change', (event) => {
        const kindSelect = event.target.closest('.nomination-row__kind');
        if (kindSelect && section.contains(kindSelect)) {
            syncCompanionDefaultPrice(kindSelect, true);
        }
    });

    section?.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-partial-form="nominations"]');
        if (!form || !section.contains(form)) {
            return;
        }

        event.preventDefault();
        window.PartialForms?.submit(form, {
            section: 'slipNominationsSection',
            modalId: 'addNominationModal',
            afterReplace: () => {
                syncCurrentDefaults();
            }
        });
    });

    syncCurrentDefaults();
    if (showAddNominationModal) {
        showAddNominationModal = false;
        showModal();
    }
})();
