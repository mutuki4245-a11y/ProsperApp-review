(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const showAdjustmentModal = pageData.showAdjustmentModal === true;

    const adjustmentModalElement = document.getElementById('adjustmentModal');
    const adjustmentModal = adjustmentModalElement ? new bootstrap.Modal(adjustmentModalElement) : null;
    const adjustmentForm = document.querySelector('[data-adjustment-form]');
    const adjustmentList = document.querySelector('[data-adjustment-list]');
    const adjustmentLinesJson = document.getElementById('adjustmentLinesJson');
    const addAdjustmentButton = document.querySelector('[data-add-adjustment-row]');
    const adjustmentStatus = document.querySelector('[data-adjustment-status]');

    if (!adjustmentModalElement || !adjustmentForm || !adjustmentList) {
        return;
    }

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

    const readRows = () => Array.from(adjustmentList.querySelectorAll('[data-adjustment-row]'))
        .map((row) => ({
            lineName: row.querySelector('[data-adjustment-name]')?.value ?? '',
            amount: row.querySelector('[data-adjustment-amount]')?.value ?? ''
        }));

    const normalizeRows = (rows) => rows
        .map((row) => {
            const amount = Number(row.amount || 0);
            return {
                lineName: (row.lineName ?? '').trim(),
                amount: Number.isFinite(amount) ? amount : row.amount
            };
        })
        .filter((row) => row.lineName || Number(row.amount || 0) !== 0);

    const readSavedRows = () => {
        if (!adjustmentList.dataset.adjustmentSavedLines) {
            return [];
        }

        try {
            const rows = JSON.parse(adjustmentList.dataset.adjustmentSavedLines);
            return Array.isArray(rows) ? normalizeRows(rows) : [];
        } catch {
            return [];
        }
    };

    let savedSnapshot = JSON.stringify(readSavedRows());
    let isSubmitting = false;

    const getSnapshot = () => JSON.stringify(normalizeRows(readRows()));

    const syncLinesJson = () => {
        if (adjustmentLinesJson) {
            adjustmentLinesJson.value = JSON.stringify(normalizeRows(readRows()));
        }
    };

    const isDirty = () => getSnapshot() !== savedSnapshot;

    const markDirty = () => {
        syncLinesJson();
        setSaveStatus(adjustmentStatus, isDirty() ? '未保存' : '保存済み');
    };

    const renumberRows = () => {
        adjustmentList.querySelectorAll('[data-adjustment-row]').forEach((row, index) => {
            const name = row.querySelector('[data-adjustment-name]');
            const amount = row.querySelector('[data-adjustment-amount]');
            if (name) {
                name.name = `AdjustmentsInput.Lines[${index}].LineName`;
            }
            if (amount) {
                amount.name = `AdjustmentsInput.Lines[${index}].Amount`;
            }
        });
    };

    const appendRow = (lineName = '', amount = '') => {
        const row = document.createElement('div');
        row.className = 'adjustment-row';
        row.dataset.adjustmentRow = '';
        row.innerHTML = `
            <input class="form-control" maxlength="160" placeholder="明細名" data-adjustment-name />
            <input class="form-control" type="number" step="1" placeholder="価格" data-adjustment-amount />
            <button class="btn btn-outline-danger" type="button" data-remove-adjustment-row>削除</button>
        `;
        row.querySelector('[data-adjustment-name]').value = lineName;
        row.querySelector('[data-adjustment-amount]').value = amount;
        adjustmentList.appendChild(row);
        renumberRows();
        syncLinesJson();
    };

    const resetToSaved = () => {
        const rows = JSON.parse(savedSnapshot);
        adjustmentList.innerHTML = '';
        rows.forEach((row) => appendRow(row.lineName ?? '', String(row.amount ?? '')));
        if (rows.length === 0) {
            appendRow();
        }
        renumberRows();
        syncLinesJson();
        setSaveStatus(adjustmentStatus, '保存済み');
    };

    addAdjustmentButton?.addEventListener('click', () => {
        appendRow();
        markDirty();
    });

    adjustmentList.addEventListener('click', (event) => {
        const removeButton = event.target.closest('[data-remove-adjustment-row]');
        if (!removeButton) {
            return;
        }

        const rows = adjustmentList.querySelectorAll('[data-adjustment-row]');
        if (rows.length <= 1) {
            const row = removeButton.closest('[data-adjustment-row]');
            const name = row?.querySelector('[data-adjustment-name]');
            const amount = row?.querySelector('[data-adjustment-amount]');
            if (name) {
                name.value = '';
            }
            if (amount) {
                amount.value = '0';
            }
        } else {
            removeButton.closest('[data-adjustment-row]')?.remove();
        }

        renumberRows();
        markDirty();
    });

    adjustmentList.addEventListener('input', markDirty);

    adjustmentForm.addEventListener('submit', () => {
        isSubmitting = true;
        setSaveStatus(adjustmentStatus, '保存中');
        renumberRows();
        syncLinesJson();
    });

    adjustmentModalElement.addEventListener('hide.bs.modal', (event) => {
        if (isSubmitting || !isDirty()) {
            return;
        }

        const confirmed = window.confirm('自由入力明細を保存せず閉じますか？');
        if (!confirmed) {
            event.preventDefault();
            return;
        }

        resetToSaved();
    });

    adjustmentModalElement.addEventListener('hidden.bs.modal', () => {
        isSubmitting = false;
    });

    if (showAdjustmentModal) {
        syncLinesJson();
        setSaveStatus(adjustmentStatus, '未保存');
        adjustmentModal?.show();
        return;
    }

    resetToSaved();
})();
