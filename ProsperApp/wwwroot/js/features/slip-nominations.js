(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const showAddNominationModal = pageData.showAddNominationModal === true;

    const addNominationModalElement = document.getElementById('addNominationModal');
    const addNominationModal = addNominationModalElement ? new bootstrap.Modal(addNominationModalElement) : null;

    if (showAddNominationModal) {
        addNominationModal?.show();
    }
})();
