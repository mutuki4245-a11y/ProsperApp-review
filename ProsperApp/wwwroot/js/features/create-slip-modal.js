(() => {
    const config = window.prosperCreateSlip ?? {};
    const createSlipModalElement = document.getElementById('createSlipModal');
    if (!createSlipModalElement) {
        return;
    }

    const createSlipForm = createSlipModalElement.querySelector('form');
    const createSlipSubmitButton = document.getElementById('businessCreateSlipSubmit');
    let castOptions = Array.isArray(config.castOptions) ? config.castOptions : [];
    let castOptionsLoaded = castOptions.length > 0;
    let castOptionsLoading = null;
    const attendanceCastsUrl = config.attendanceCastsUrl || '';
    const showCreateSlipModal = Boolean(config.showCreateSlipModal);
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

        if (!castOptionsLoading) {
            castOptionsLoading = fetch(attendanceCastsUrl, {
                headers: {
                    'Accept': 'application/json'
                }
            })
                .then((response) => {
                    if (!response.ok) {
                        throw new Error('Attendance casts load failed.');
                    }

                    return response.json();
                })
                .then((items) => {
                    castOptions = Array.isArray(items) ? items : [];
                    castOptionsLoaded = true;
                    castOptionsLoading = null;
                    return castOptions;
                })
                .catch(() => {
                    castOptions = [];
                    castOptionsLoaded = false;
                    castOptionsLoading = null;
                    return castOptions;
                });
        }

        return castOptionsLoading;
    };

    document.querySelectorAll('[data-business-slip-table-id]').forEach((button) => {
        button.addEventListener('click', () => {
            if (tableIdInput) {
                tableIdInput.value = button.dataset.businessSlipTableId ?? '';
            }
            document.querySelectorAll('[data-business-slip-table-id]').forEach((target) => {
                target.classList.toggle('is-selected', target === button);
            });
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

    const addCustomerRow = () => {
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
        row.querySelector('input')?.focus();
        renumberCustomerRows();
    };

    const removeLastCustomerRow = () => {
        const rows = getCustomerRows();
        if (rows.length <= 1) {
            return;
        }

        rows[rows.length - 1]?.remove();
        renumberCustomerRows();
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

    const addNominationRow = () => {
        if (!nominationList || nominationKindOptions.length === 0 || availableNominationCasts().length === 0) {
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
    };

    const removeLastNominationRow = () => {
        const rows = getNominationRows();
        if (rows.length === 0) {
            return;
        }

        rows[rows.length - 1]?.remove();
        renumberNominationRows();
    };

    decreaseCustomerCountButton?.addEventListener('click', removeLastCustomerRow);
    increaseCustomerCountButton?.addEventListener('click', addCustomerRow);
    decreaseNominationCountButton?.addEventListener('click', removeLastNominationRow);
    increaseNominationCountButton?.addEventListener('click', addNominationRow);

    let isExplicitCreateSlipSubmit = false;
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
    createSlipForm?.addEventListener('submit', (event) => {
        if (!isExplicitCreateSlipSubmit) {
            event.preventDefault();
            return;
        }

        isExplicitCreateSlipSubmit = false;
    });
    createSlipSubmitButton?.addEventListener('click', () => {
        if (!createSlipForm) {
            return;
        }

        isExplicitCreateSlipSubmit = true;
        createSlipForm.requestSubmit();
        window.queueMicrotask(() => {
            isExplicitCreateSlipSubmit = false;
        });
    });

    renumberCustomerRows();
    renumberNominationRows();
    nominationList?.querySelectorAll('[data-business-nomination-row]').forEach(wireNominationRow);
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
