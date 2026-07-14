(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const showAdjustmentModal = pageData.showAdjustmentModal === true;

    const adjustmentModalElement = document.getElementById('adjustmentModal');
    const adjustmentModal = adjustmentModalElement ? new bootstrap.Modal(adjustmentModalElement) : null;
    const adjustmentForm = document.querySelector('[data-adjustment-form]');
    const nameInput = document.querySelector('[data-adjustment-name]');
    const amountInput = document.querySelector('[data-adjustment-amount]');
    const adjustmentStatus = document.querySelector('[data-adjustment-status]');

    if (!adjustmentModalElement || !adjustmentForm || !nameInput || !amountInput) {
        return;
    }

    const saveStatus = window.TerminalSaveStatus;
    let isSubmitting = false;

    const readInput = () => ({
        lineName: (nameInput.value ?? '').trim(),
        amount: Number(amountInput.value || 0)
    });

    const hasInput = () => {
        const input = readInput();
        return input.lineName.length > 0 || input.amount !== 0;
    };

    const markStatus = () => {
        if (hasInput()) {
            saveStatus.dirty(adjustmentStatus);
        } else {
            saveStatus.saved(adjustmentStatus);
        }
    };

    const resetInput = () => {
        nameInput.value = '';
        amountInput.value = '0';
        saveStatus.saved(adjustmentStatus);
    };

    nameInput.addEventListener('input', markStatus);
    amountInput.addEventListener('input', markStatus);

    adjustmentForm.addEventListener('submit', () => {
        isSubmitting = true;
        saveStatus.saving(adjustmentStatus);
    });

    adjustmentModalElement.addEventListener('hide.bs.modal', (event) => {
        if (isSubmitting || !hasInput()) {
            return;
        }

        const confirmed = window.confirm('自由入力明細を保存せず閉じますか？');
        if (!confirmed) {
            event.preventDefault();
            return;
        }

        resetInput();
    });

    adjustmentModalElement.addEventListener('hidden.bs.modal', () => {
        isSubmitting = false;
    });

    if (showAdjustmentModal) {
        markStatus();
        adjustmentModal?.show();
        return;
    }

    markStatus();
})();
