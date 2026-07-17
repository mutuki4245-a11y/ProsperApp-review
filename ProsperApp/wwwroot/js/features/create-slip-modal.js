(() => {
    const config = window.prosperCreateSlip ?? {};
    const createSlipModalElement = document.getElementById('createSlipModal');
    if (!createSlipModalElement) {
        return;
    }

    let castOptions = Array.isArray(config.castOptions) ? config.castOptions : [];
    let castOptionsLoaded = castOptions.length > 0;
    let castOptionsLoading = null;
    const attendanceCastsUrl = config.attendanceCastsUrl || '';
    const showCreateSlipModal = Boolean(config.showCreateSlipModal);
    const tableIdInput = document.getElementById('businessCreateSlipTableId');
    const customerList = document.getElementById('businessCustomerList');
    const addCustomerButton = document.getElementById('businessAddCustomerButton');
    const nominationList = document.getElementById('businessNominationList');
    const nominationEmpty = document.querySelector('[data-business-nomination-empty]');
    const addNominationButton = document.getElementById('businessAddNominationButton');
    const castModalElement = document.getElementById('businessAttendingCastSelectModal');
    const castModalList = document.getElementById('businessAttendingCastModalList');
    const createSlipModal = new bootstrap.Modal(createSlipModalElement);
    const castModal = castModalElement ? new bootstrap.Modal(castModalElement) : null;
    let castModalTargetRow = null;
    const nominationKindOptions = Array.isArray(config.nominationKindOptions) ? config.nominationKindOptions : [];
    const escapeHtml = (value) => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
    const nominationKindOptionsHtml = [
        '<option value="">指名区分</option>',
        ...nominationKindOptions.map((option, index) => `<option value="${escapeHtml(option.value)}" ${index === 0 ? 'selected' : ''} data-companion="${option.isCompanion === true ? 'true' : 'false'}">${escapeHtml(option.label)}</option>`)
    ].join('');
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
    };

    const renumberCustomerRows = () => {
        if (!customerList) {
            return;
        }

        const rows = customerList.querySelectorAll('[data-business-customer-row]');
        rows.forEach((row, index) => {
            const label = row.querySelector('.customer-row__index');
            const input = row.querySelector('input');
            const remove = row.querySelector('[data-business-remove-customer]');
            if (label) {
                label.textContent = String(index + 1);
            }
            if (input) {
                input.name = `CreateSlipInput.CustomerLabels[${index}]`;
            }
            if (remove) {
                remove.hidden = index === 0;
            }
        });
    };

    const addCustomerRow = () => {
        if (!customerList) {
            return;
        }

        const count = customerList.querySelectorAll('[data-business-customer-row]').length;
        if (count >= 20) {
            return;
        }

        const row = document.createElement('div');
        row.className = 'customer-row';
        row.dataset.businessCustomerRow = '';
        row.innerHTML = `
            <label class="customer-row__index">${count + 1}</label>
            <input class="form-control form-control-lg" name="CreateSlipInput.CustomerLabels[${count}]" maxlength="100" placeholder="客名・特徴など" />
            <button class="btn btn-outline-danger customer-row__remove" type="button" data-business-remove-customer>削除</button>
        `;
        customerList.appendChild(row);
        row.querySelector('input')?.focus();
        renumberCustomerRows();
    };

    const renderCastModal = () => {
        window.CastSelectModal.renderRequired(castModalList, castOptions, {
            getLabel: (cast) => cast.display,
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
        renderCastModal();
        castModal?.show();
    };

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

        if (nominationKindOptions.length === 0) {
            return;
        }

        const rows = nominationList.querySelectorAll('[data-business-nomination-row]');
        if (nominationEmpty) {
            nominationEmpty.hidden = rows.length > 0;
        }
        rows.forEach((row, index) => {
            const label = row.querySelector('.nomination-row__index');
            const kind = row.querySelector('.nomination-row__kind');
            const price = row.querySelector('.nomination-row__price');
            const castId = row.querySelector('[data-business-cast-id]');
            const castName = row.querySelector('[data-business-cast-name-hidden]');
            const remove = row.querySelector('[data-business-remove-nomination]');
            if (label) {
                label.textContent = String(index + 1);
            }
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
            if (remove) {
                remove.hidden = index === 0;
            }
        });
    };

    const addNominationRow = async () => {
        if (!nominationList) {
            return;
        }

        await loadCastOptions();
        if (castOptions.length === 0) {
            renderCastModal();
            castModal?.show();
            return;
        }

        const count = nominationList.querySelectorAll('[data-business-nomination-row]').length;
        if (count >= 20) {
            return;
        }

        const row = document.createElement('div');
        row.className = 'nomination-row';
        row.dataset.businessNominationRow = '';
        row.innerHTML = `
            <label class="customer-row__index nomination-row__index">${count + 1}</label>
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
            <button class="btn btn-outline-danger nomination-row__remove" type="button" data-business-remove-nomination>削除</button>
        `;
        nominationList.appendChild(row);
        wireNominationRow(row);
        renumberNominationRows();
        await openCastModal(row);
    };

    customerList?.addEventListener('click', (event) => {
        const button = event.target.closest('[data-business-remove-customer]');
        if (!button || !customerList.contains(button)) {
            return;
        }

        button.closest('[data-business-customer-row]')?.remove();
        renumberCustomerRows();
    });

    nominationList?.addEventListener('click', (event) => {
        const button = event.target.closest('[data-business-remove-nomination]');
        if (!button || !nominationList.contains(button)) {
            return;
        }

        button.closest('[data-business-nomination-row]')?.remove();
        renumberNominationRows();
    });

    addCustomerButton?.addEventListener('click', addCustomerRow);
    addNominationButton?.addEventListener('click', () => {
        void addNominationRow();
    });
    renumberCustomerRows();
    renumberNominationRows();
    nominationList?.querySelectorAll('[data-business-nomination-row]').forEach(wireNominationRow);
    castModalElement?.addEventListener('hidden.bs.modal', () => {
        castModalTargetRow = null;
    });
    createSlipModalElement.addEventListener('shown.bs.modal', () => {
        void loadCastOptions();
    });

    if (showCreateSlipModal) {
        createSlipModal.show();
    }
})();
