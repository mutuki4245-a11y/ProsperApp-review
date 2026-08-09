(() => {
    const config = window.prosperCreateSlip ?? {};
    const createSlipModalElement = document.getElementById('createSlipModal');
    if (!createSlipModalElement) {
        return;
    }

    const createSlipForm = createSlipModalElement.querySelector('form');
    const createSlipSubmitButton = document.getElementById('businessCreateSlipSubmit');
    const createSlipOpenButton = document.querySelector('[data-business-create-slip-button]');
    const createSlipWarning = document.querySelector('[data-business-create-slip-warning]');
    let castOptions = Array.isArray(config.castOptions) ? config.castOptions : [];
    if (Array.isArray(window.ProsperCurrentAttendanceCasts)) {
        castOptions = window.ProsperCurrentAttendanceCasts.map((cast) => ({
            id: cast.cast_id ?? cast.id,
            name: cast.display_name ?? cast.name,
            display: cast.search_display_name ?? cast.display ?? cast.display_name ?? cast.name,
            department: cast.department_name ?? cast.department
        }));
    }
    let castOptionsLoaded = castOptions.length > 0;
    let castOptionsLoading = null;
    const showCreateSlipModal = Boolean(config.showCreateSlipModal);
    const departmentId = window.ProsperBusinessHomeConfig?.departmentId;
    const draftStorageKey = departmentId ? `prosper:create-slip-draft:v2:${departmentId}` : '';
    const saveResponse = window.ProsperSaveResponse;
    let pendingCreateCommand = null;
    let isSubmitting = false;
    let isRestoringDraft = false;
    let persistCreateDraft = () => {};

    const syncCreateAvailability = () => {
        const available = castOptions.length > 0;
        if (createSlipOpenButton) createSlipOpenButton.disabled = !available;
        if (createSlipSubmitButton) {
            createSlipSubmitButton.disabled = isSubmitting || !available ||
                !document.getElementById('businessCreateSlipTableId')?.value;
        }
        if (createSlipWarning) createSlipWarning.hidden = available;
    };

    document.addEventListener('prosper:attendance-casts-updated', (event) => {
        castOptions = Array.isArray(event.detail?.casts)
            ? event.detail.casts.map((cast) => ({
                id: cast.cast_id ?? cast.id,
                name: cast.display_name ?? cast.name,
                display: cast.search_display_name ?? cast.display ?? cast.display_name ?? cast.name,
                department: cast.department_name ?? cast.department
            }))
            : [];
        castOptionsLoaded = true;
        castOptionsLoading = null;
        syncCreateAvailability();
    });

    syncCreateAvailability();
    const tableIdInput = document.getElementById('businessCreateSlipTableId');
    const customerList = document.getElementById('businessCustomerList');
    const customerCountDisplay = document.getElementById('businessCustomerCount');
    const decreaseCustomerCountButton = document.getElementById('businessDecreaseCustomerCount');
    const increaseCustomerCountButton = document.getElementById('businessIncreaseCustomerCount');
    const nominationList = document.getElementById('businessNominationList');
    const nominationEmpty = document.querySelector('[data-business-nomination-empty]');
    const nominationCountDisplay = document.getElementById('businessNominationCount');
    const decreaseNominationCountButton = document.getElementById('businessDecreaseNominationCount');
    const increaseNominationCountButton = document.getElementById('businessIncreaseNominationCount');
    const castModalElement = document.getElementById('businessAttendingCastSelectModal');
    const castModalList = document.getElementById('businessAttendingCastModalList');
    const castModalDuplicateFilter = document.getElementById('businessAttendingCastDuplicateFilter');
    const castModalAllowDuplicates = document.getElementById('businessAttendingCastAllowNominationDuplicates');
    const createSlipModal = bootstrap.Modal.getOrCreateInstance(createSlipModalElement);
    const castModal = castModalElement ? bootstrap.Modal.getOrCreateInstance(castModalElement) : null;
    let castModalTargetRow = null;
    const nominationKindOptions = Array.isArray(config.nominationKindOptions) ? config.nominationKindOptions : [];
    const escapeHtml = (value) => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
    const nominationKindOptionsHtml = nominationKindOptions
        .map((option, index) => `<option value="${escapeHtml(option.value)}" ${index === 0 ? 'selected' : ''} data-companion="${option.isCompanion === true ? 'true' : 'false'}">${escapeHtml(option.label)}</option>`)
        .join('');
    const nominationPriceOptions = Array.isArray(config.nominationPriceOptions) ? config.nominationPriceOptions : [];
    const nominationPriceOptionsHtml = nominationPriceOptions
        .map((price) => `<option value="${price.value}">${price.label}</option>`)
        .join('');
    const companionNominationPrice = '3000';

    const syncNominationDefaultPrice = (kindSelect, force = false) => {
        const selectedOption = kindSelect?.selectedOptions?.[0];
        if (!selectedOption || selectedOption.dataset.companion !== 'true') {
            return;
        }

        const priceSelect = kindSelect.closest('[data-business-nomination-row]')?.querySelector('.nomination-row__price');
        if (!priceSelect || !Array.from(priceSelect.options).some((option) => option.value === companionNominationPrice)) {
            return;
        }

        if (force || priceSelect.value === '' || priceSelect.value === '1000') {
            priceSelect.value = companionNominationPrice;
        }
    };

    const loadCastOptions = async () => {
        if (castOptionsLoaded) {
            return castOptions;
        }

        return castOptions;
    };

    document.querySelectorAll('[data-business-slip-table-id]').forEach((button) => {
        button.addEventListener('click', () => {
            if (tableIdInput) {
                tableIdInput.value = button.dataset.businessSlipTableId ?? '';
            }
            document.querySelectorAll('[data-business-slip-table-id]').forEach((target) => {
                target.classList.toggle('is-selected', target === button);
            });
            persistCreateDraft();
            syncCreateAvailability();
        });
    });

    const setSelectedCast = (row, cast) => {
        const hiddenInput = row.querySelector('[data-business-cast-id]');
        const hiddenName = row.querySelector('[data-business-cast-name-hidden]');
        const selected = row.querySelector('[data-business-selected-cast]');
        if (!hiddenInput || !hiddenName || !selected) {
            return;
        }

        hiddenInput.value = cast.id;
        hiddenName.value = cast.display;
        selected.textContent = cast.display;
        renumberNominationRows();
        persistCreateDraft();
    };

    const getCustomerRows = () => customerList?.querySelectorAll('[data-business-customer-row]') ?? [];

    const syncCustomerCountControls = () => {
        const count = getCustomerRows().length;
        if (customerCountDisplay) {
            customerCountDisplay.textContent = `${count}人`;
        }
        if (decreaseCustomerCountButton) {
            decreaseCustomerCountButton.disabled = count <= 1;
        }
        if (increaseCustomerCountButton) {
            increaseCustomerCountButton.disabled = count >= 20;
        }
    };

    const renumberCustomerRows = () => {
        if (!customerList) {
            return;
        }

        const rows = getCustomerRows();
        rows.forEach((row, index) => {
            const input = row.querySelector('input');
            if (input) {
                input.name = `CreateSlipInput.CustomerLabels[${index}]`;
            }
        });
        syncCustomerCountControls();
    };

    const addCustomerRow = (focus = true) => {
        if (!customerList) {
            return;
        }

        const count = getCustomerRows().length;
        if (count >= 20) {
            return;
        }

        const row = document.createElement('div');
        row.className = 'customer-row';
        row.dataset.businessCustomerRow = '';
        row.innerHTML = `
            <input class="form-control form-control-lg" name="CreateSlipInput.CustomerLabels[${count}]" maxlength="100" placeholder="お客様名" />
        `;
        customerList.appendChild(row);
        if (focus) {
            row.querySelector('input')?.focus();
        }
        renumberCustomerRows();
        persistCreateDraft();
    };

    const removeLastCustomerRow = () => {
        const rows = getCustomerRows();
        if (rows.length <= 1) {
            return;
        }

        rows[rows.length - 1]?.remove();
        renumberCustomerRows();
        persistCreateDraft();
    };

    const selectedNominationCastIds = () => new Set(
        Array.from(nominationList?.querySelectorAll('[data-business-nomination-row]') ?? [])
            .map((row) => row.querySelector('[data-business-cast-id]')?.value || '')
            .filter(Boolean)
            .map(String)
    );

    const availableNominationCasts = () => {
        const selectedCastIds = selectedNominationCastIds();
        return castOptions.filter((cast) => !selectedCastIds.has(String(cast.id)));
    };

    const renderCastModal = () => {
        window.CastSelectModal.renderRequired(castModalList, availableNominationCasts(), {
            getLabel: (cast) => cast.display,
            emptyMessage: '選択できる出勤キャストがありません。',
            onSelect: (cast) => {
                if (castModalTargetRow) {
                    setSelectedCast(castModalTargetRow, cast);
                }
                castModal?.hide();
            }
        });
    };

    const openCastModal = async (row) => {
        castModalTargetRow = row;
        await loadCastOptions();
        renumberNominationRows();
        if (castModalDuplicateFilter) castModalDuplicateFilter.hidden = true;
        if (castModalAllowDuplicates) castModalAllowDuplicates.checked = false;
        renderCastModal();
        createSlipModalElement.classList.add('is-child-modal-active');
        castModal?.show();
    };

    castModalElement?.addEventListener('hidden.bs.modal', () => {
        createSlipModalElement.classList.remove('is-child-modal-active');
        castModalTargetRow = null;
    });

    const wireNominationRow = (row) => {
        const kindSelect = row.querySelector('.nomination-row__kind');
        kindSelect?.addEventListener('change', () => {
            syncNominationDefaultPrice(kindSelect, true);
        });
        if (kindSelect) {
            syncNominationDefaultPrice(kindSelect);
        }
        row.querySelector('[data-business-open-cast-modal]')?.addEventListener('click', () => {
            void openCastModal(row);
        });
    };

    const renumberNominationRows = () => {
        if (!nominationList) {
            return;
        }

        const rows = getNominationRows();
        if (nominationEmpty) {
            nominationEmpty.hidden = rows.length > 0;
        }
        if (nominationCountDisplay) {
            nominationCountDisplay.textContent = `${rows.length}人`;
        }
        if (decreaseNominationCountButton) {
            decreaseNominationCountButton.disabled = rows.length === 0;
        }
        if (increaseNominationCountButton) {
            increaseNominationCountButton.disabled =
                nominationKindOptions.length === 0 ||
                rows.length >= 20 ||
                availableNominationCasts().length === 0;
        }
        rows.forEach((row, index) => {
            const kind = row.querySelector('.nomination-row__kind');
            const price = row.querySelector('.nomination-row__price');
            const castId = row.querySelector('[data-business-cast-id]');
            const castName = row.querySelector('[data-business-cast-name-hidden]');
            if (kind) {
                kind.name = `CreateSlipInput.CastNominations[${index}].NominationKind`;
            }
            if (price) {
                price.name = `CreateSlipInput.CastNominations[${index}].NominationPrice`;
            }
            if (castId) {
                castId.name = `CreateSlipInput.CastNominations[${index}].CastId`;
            }
            if (castName) {
                castName.name = `CreateSlipInput.CastNominations[${index}].CastName`;
            }
        });
    };

    const getNominationRows = () => nominationList?.querySelectorAll('[data-business-nomination-row]') ?? [];

    const addNominationRow = (allowUnavailable = false) => {
        if (!nominationList || nominationKindOptions.length === 0 ||
            (!allowUnavailable && availableNominationCasts().length === 0)) {
            return;
        }

        const count = getNominationRows().length;
        if (count >= 20) {
            return;
        }

        const row = document.createElement('div');
        row.className = 'nomination-row';
        row.dataset.businessNominationRow = '';
        row.innerHTML = `
            <select class="form-select nomination-row__kind" name="CreateSlipInput.CastNominations[${count}].NominationKind">
                ${nominationKindOptionsHtml}
            </select>
            <select class="form-select nomination-row__price" name="CreateSlipInput.CastNominations[${count}].NominationPrice">
                ${nominationPriceOptionsHtml}
            </select>
            <div class="nomination-row__cast">
                <input type="hidden" name="CreateSlipInput.CastNominations[${count}].CastId" data-business-cast-id />
                <input type="hidden" name="CreateSlipInput.CastNominations[${count}].CastName" data-business-cast-name-hidden />
                <button class="selected-cast selected-cast--button" type="button" data-business-open-cast-modal>
                    <span data-business-selected-cast>キャストを選択</span>
                </button>
            </div>
        `;
        nominationList.appendChild(row);
        wireNominationRow(row);
        renumberNominationRows();
        persistCreateDraft();
    };

    const removeLastNominationRow = () => {
        const rows = getNominationRows();
        if (rows.length === 0) {
            return;
        }

        rows[rows.length - 1]?.remove();
        renumberNominationRows();
        persistCreateDraft();
    };

    decreaseCustomerCountButton?.addEventListener('click', removeLastCustomerRow);
    increaseCustomerCountButton?.addEventListener('click', () => addCustomerRow());
    decreaseNominationCountButton?.addEventListener('click', removeLastNominationRow);
    increaseNominationCountButton?.addEventListener('click', () => addNominationRow());

    const readCreateDraft = () => {
        if (!draftStorageKey) {
            return null;
        }

        try {
            const draft = JSON.parse(localStorage.getItem(draftStorageKey) || 'null');
            return draft?.version === 2 ? draft : null;
        } catch {
            try {
                localStorage.removeItem(draftStorageKey);
            } catch {
                // localStorageを利用できない端末では画面内の入力だけを保持する。
            }
            return null;
        }
    };

    const clearCreateDraft = () => {
        if (!draftStorageKey) {
            return;
        }

        try {
            localStorage.removeItem(draftStorageKey);
        } catch {
            // localStorageを利用できない端末では画面内の入力だけを保持する。
        }
    };

    const snapshotCreateDraft = () => ({
        version: 2,
        departmentId,
        tableId: Number(tableIdInput?.value || 0) || null,
        businessDate: createSlipForm?.querySelector('[data-business-create-date-input]')?.value || null,
        openedTime: createSlipForm?.querySelector('[name="CreateSlipInput.OpenedTime"]')?.value || null,
        customerLabels: Array.from(createSlipForm?.querySelectorAll('[data-business-customer-row] input') || [])
            .map((input) => input.value),
        nominations: Array.from(getNominationRows()).map((row) => ({
            castId: Number(row.querySelector('[data-business-cast-id]')?.value || 0) || null,
            castName: row.querySelector('[data-business-cast-name-hidden]')?.value || null,
            nominationKind: row.querySelector('.nomination-row__kind')?.value || null,
            nominationPrice: row.querySelector('.nomination-row__price')?.value || null
        })),
        memo: createSlipForm?.querySelector('[name="CreateSlipInput.Memo"]')?.value || '',
        pendingCreateCommand
    });

    persistCreateDraft = () => {
        if (!draftStorageKey || isRestoringDraft) {
            return;
        }

        const draft = snapshotCreateDraft();
        const hasInput = Boolean(
            draft.pendingCreateCommand ||
            draft.tableId ||
            draft.memo.trim() ||
            draft.nominations.length > 0 ||
            draft.customerLabels.some((label) => label.trim()));
        if (!hasInput) {
            clearCreateDraft();
            return;
        }

        try {
            localStorage.setItem(draftStorageKey, JSON.stringify(draft));
        } catch {
            // 保存できなくても現在の画面内入力はそのまま利用する。
        }
    };

    const syncPendingCommandControls = () => {
        const locked = pendingCreateCommand !== null;
        createSlipForm?.querySelectorAll(
            'input:not([type="hidden"]), select, textarea, [data-business-slip-table-id], [data-business-open-cast-modal]'
        ).forEach((control) => {
            control.disabled = locked;
        });
        decreaseCustomerCountButton && (decreaseCustomerCountButton.disabled = locked || getCustomerRows().length <= 1);
        increaseCustomerCountButton && (increaseCustomerCountButton.disabled = locked || getCustomerRows().length >= 20);
        decreaseNominationCountButton && (decreaseNominationCountButton.disabled = locked || getNominationRows().length === 0);
        if (!locked) {
            renumberNominationRows();
        }
        syncCreateAvailability();
    };

    const setPendingCreateCommand = (command) => {
        pendingCreateCommand = command;
        syncPendingCommandControls();
        persistCreateDraft();
    };

    const restoreCreateDraft = () => {
        const draft = readCreateDraft();
        if (!draft || !createSlipForm) {
            return;
        }

        isRestoringDraft = true;
        try {
            if (tableIdInput) {
                tableIdInput.value = draft.tableId || '';
            }
            document.querySelectorAll('[data-business-slip-table-id]').forEach((button) => {
                button.classList.toggle('is-selected', String(button.dataset.businessSlipTableId) === String(draft.tableId));
            });

            const openedTime = createSlipForm.querySelector('[name="CreateSlipInput.OpenedTime"]');
            const businessDate = createSlipForm.querySelector('[data-business-create-date-input]');
            const memo = createSlipForm.querySelector('[name="CreateSlipInput.Memo"]');
            if (openedTime && draft.openedTime) openedTime.value = draft.openedTime;
            if (businessDate && draft.businessDate) businessDate.value = draft.businessDate;
            if (memo) memo.value = draft.memo || '';

            const customerLabels = Array.isArray(draft.customerLabels) && draft.customerLabels.length > 0
                ? draft.customerLabels.slice(0, 20)
                : [''];
            while (getCustomerRows().length < customerLabels.length) addCustomerRow(false);
            while (getCustomerRows().length > customerLabels.length) getCustomerRows()[getCustomerRows().length - 1]?.remove();
            Array.from(getCustomerRows()).forEach((row, index) => {
                const input = row.querySelector('input');
                if (input) input.value = customerLabels[index] || '';
            });

            nominationList?.querySelectorAll('[data-business-nomination-row]').forEach((row) => row.remove());
            (Array.isArray(draft.nominations) ? draft.nominations.slice(0, 20) : []).forEach((nomination) => {
                addNominationRow(true);
                const row = getNominationRows()[getNominationRows().length - 1];
                if (!row) return;
                const kind = row.querySelector('.nomination-row__kind');
                const price = row.querySelector('.nomination-row__price');
                if (kind && nomination.nominationKind) kind.value = nomination.nominationKind;
                if (price && nomination.nominationPrice) price.value = nomination.nominationPrice;
                if (nomination.castId) {
                    setSelectedCast(row, {
                        id: nomination.castId,
                        display: nomination.castName || `キャスト ${nomination.castId}`
                    });
                }
            });
            pendingCreateCommand = draft.pendingCreateCommand || null;
        } finally {
            isRestoringDraft = false;
        }
        renumberCustomerRows();
        renumberNominationRows();
        syncPendingCommandControls();
    };

    const resetCreateDraftForm = () => {
        createSlipForm?.reset();
        if (tableIdInput) tableIdInput.value = '';
        document.querySelectorAll('[data-business-slip-table-id]').forEach((button) => button.classList.remove('is-selected'));
        while (getCustomerRows().length > 1) getCustomerRows()[getCustomerRows().length - 1]?.remove();
        nominationList?.querySelectorAll('[data-business-nomination-row]').forEach((row) => row.remove());
        renumberCustomerRows();
        renumberNominationRows();
    };

    createSlipForm?.addEventListener('keydown', (event) => {
        const target = event.target;
        if (
            event.key !== 'Enter' ||
            event.isComposing ||
            target instanceof HTMLTextAreaElement ||
            target instanceof HTMLButtonElement ||
            !(target instanceof HTMLInputElement || target instanceof HTMLSelectElement)
        ) {
            return;
        }

        event.preventDefault();
    });
    createSlipForm?.addEventListener('submit', (event) => event.preventDefault());
    createSlipForm?.addEventListener('input', persistCreateDraft);
    createSlipForm?.addEventListener('change', persistCreateDraft);

    const addBusinessDays = (dateText, days) => {
        const date = new Date(`${dateText}T00:00:00+09:00`);
        date.setUTCDate(date.getUTCDate() + days);
        return new Intl.DateTimeFormat('en-CA', {
            timeZone: 'Asia/Tokyo', year: 'numeric', month: '2-digit', day: '2-digit'
        }).format(date);
    };

    const buildCreateCommand = () => {
        const state = window.ProsperBusinessHomeState || {};
        const tableId = Number(tableIdInput?.value || 0);
        const openedTime = createSlipForm?.querySelector('[name="CreateSlipInput.OpenedTime"]')?.value || '';
        const businessDate = createSlipForm?.querySelector('[data-business-create-date-input]')?.value || state.businessDate;
        if (!tableId || !/^([01]\d|2[0-3]):[0-5]\d$/.test(openedTime) || !/^\d{4}-\d{2}-\d{2}$/.test(businessDate || '')) {
            throw new Error('卓番と入店時刻を確認してください。');
        }
        const calendarDate = openedTime < '12:00' ? addBusinessDays(businessDate, 1) : businessDate;
        const customerLabels = Array.from(createSlipForm.querySelectorAll('[data-business-customer-row] input'))
            .map((input) => input.value.trim() || null);
        const castNominations = Array.from(getNominationRows()).map((row) => ({
            castId: Number(row.querySelector('[data-business-cast-id]')?.value || 0) || null,
            nominationKind: row.querySelector('.nomination-row__kind')?.value || null,
            nominationPrice: Number(row.querySelector('.nomination-row__price')?.value || 0)
        }));
        return {
            operationId: crypto.randomUUID(),
            clientDraftId: crypto.randomUUID(),
            slipId: null,
            operationType: 'create_slip',
            businessDate,
            payload: {
                table_id: tableId,
                opened_at: new Date(`${calendarDate}T${openedTime}:00+09:00`).toISOString(),
                customer_labels: customerLabels,
                cast_nominations: castNominations.map((nomination) => ({
                    cast_id: nomination.castId,
                    nomination_kind: nomination.nominationKind,
                    nomination_price: nomination.nominationPrice
                })),
                memo: createSlipForm.querySelector('[name="CreateSlipInput.Memo"]')?.value?.trim() || null
            }
        };
    };

    const showCreateError = (message) => {
        const summary = createSlipForm?.querySelector('[data-valmsg-summary="true"], .validation-summary-valid, .validation-summary-errors');
        if (!summary) return;
        summary.classList.remove('validation-summary-valid');
        summary.classList.add('validation-summary-errors');
        summary.replaceChildren(Object.assign(document.createElement('div'), { textContent: message }));
    };

    createSlipSubmitButton?.addEventListener('click', async () => {
        if (!createSlipForm) {
            return;
        }
        try {
            if (!pendingCreateCommand) {
                setPendingCreateCommand(buildCreateCommand());
            }
            isSubmitting = true;
            syncCreateAvailability();
            const result = await window.ProsperBusinessHome?.enqueueAndFlushOperation?.(pendingCreateCommand);
            if (result?.status !== 'confirmed' || result?.operationResult?.succeeded !== true) {
                const failedOperation = result?.operationResult ||
                    result?.operationResults?.find?.((row) => row?.succeeded === false);
                const definitive = saveResponse?.isRejectedStatus(result?.status) ||
                    saveResponse?.isRejectedStatus(failedOperation?.status);
                if (definitive) {
                    window.ProsperBusinessHome?.discardOperation?.(pendingCreateCommand.operationId);
                    setPendingCreateCommand(null);
                }
                throw new Error(
                    result?.operationResult?.message ||
                    result?.operationResults?.find?.((row) => row?.succeeded === false)?.message ||
                    result?.message ||
                    '伝票を作成できませんでした。');
            }
            setPendingCreateCommand(null);
            clearCreateDraft();
            createSlipModal.hide();
            resetCreateDraftForm();
        } catch (error) {
            showCreateError(error?.message || '伝票を作成できませんでした。');
        } finally {
            isSubmitting = false;
            syncCreateAvailability();
        }
    });

    renumberCustomerRows();
    renumberNominationRows();
    nominationList?.querySelectorAll('[data-business-nomination-row]').forEach(wireNominationRow);
    restoreCreateDraft();
    document.addEventListener('prosper:business-home-flush-completed', (event) => {
        const completedCreate = event.detail?.operations?.find((operation) =>
            operation?.operationType === 'create_slip' &&
            String(operation.operationId) === String(pendingCreateCommand?.operationId));
        const confirmed = event.detail?.operationResults?.some((result) =>
            result?.succeeded === true &&
            String(result.operation_id) === String(completedCreate?.operationId));
        if (!completedCreate || !confirmed) {
            return;
        }

        pendingCreateCommand = null;
        clearCreateDraft();
        resetCreateDraftForm();
        syncPendingCommandControls();
        createSlipModal.hide();
    });
    castModalElement?.addEventListener('hidden.bs.modal', () => {
        castModalTargetRow = null;
    });
    createSlipModalElement.addEventListener('shown.bs.modal', async () => {
        await loadCastOptions();
        renumberNominationRows();
    });

    if (showCreateSlipModal) {
        createSlipModal.show();
    }
})();
