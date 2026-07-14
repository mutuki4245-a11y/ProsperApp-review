(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const castOptions = pageData.castOptions ?? [];
    const showAddNominationModal = pageData.showAddNominationModal === true;

    const attendingCastModalElement = document.getElementById('attendingCastSelectModal');
    const attendingCastModalList = document.getElementById('attendingCastModalList');
    const attendingCastModal = attendingCastModalElement ? new bootstrap.Modal(attendingCastModalElement) : null;
    const addNominationModalElement = document.getElementById('addNominationModal');
    const addNominationModal = addNominationModalElement ? new bootstrap.Modal(addNominationModalElement) : null;
    let castModalTargetRow = null;

    const getNominationList = () => document.getElementById('nominationList');

    const renumberNominations = () => {
        const nominationList = getNominationList();
        if (!nominationList) {
            return;
        }

        nominationList.querySelectorAll('[data-nomination-row]').forEach((row, index) => {
            const label = row.querySelector('.nomination-row__index');
            const kind = row.querySelector('.nomination-row__kind');
            const price = row.querySelector('.nomination-row__price');
            const castId = row.querySelector('[data-cast-id]');
            const castName = row.querySelector('[data-cast-name-hidden]');
            const removeButton = row.querySelector('[data-remove-nomination]');
            if (label) {
                label.textContent = String(index + 1);
            }
            if (kind) {
                kind.name = 'AddNominationsInput.CastNominations[0].NominationKind';
            }
            if (price) {
                price.name = 'AddNominationsInput.CastNominations[0].NominationPrice';
            }
            if (castId) {
                castId.name = 'AddNominationsInput.CastNominations[0].CastId';
            }
            if (castName) {
                castName.name = 'AddNominationsInput.CastNominations[0].CastName';
            }
            if (removeButton) {
                removeButton.hidden = index === 0;
            }
        });
    };

    const setSelectedCast = (row, cast) => {
        const hiddenInput = row.querySelector('[data-cast-id]');
        const hiddenName = row.querySelector('[data-cast-name-hidden]');
        const selected = row.querySelector('[data-selected-cast]');
        if (!hiddenInput || !hiddenName || !selected) {
            return;
        }

        hiddenInput.value = cast.id;
        const nominationDisplay = cast.nominationDisplay ?? cast.display;
        hiddenName.value = nominationDisplay;
        selected.textContent = nominationDisplay;
    };

    const renderAttendingCastModal = () => {
        if (!attendingCastModalList) {
            return;
        }

        attendingCastModalList.innerHTML = '';
        const matches = castOptions.slice(0, 80);
        if (matches.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'text-muted mb-0';
            empty.textContent = '出勤キャストが登録されていません。';
            attendingCastModalList.appendChild(empty);
            return;
        }

        matches.forEach((cast) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'cast-select-modal__item';
            const name = document.createElement('strong');
            name.textContent = cast.nominationDisplay ?? cast.display;
            button.append(name);
            button.addEventListener('click', () => {
                if (castModalTargetRow) {
                    setSelectedCast(castModalTargetRow, cast);
                }
                attendingCastModal?.hide();
            });
            attendingCastModalList.appendChild(button);
        });
    };

    const openCastModal = (row) => {
        castModalTargetRow = row;
        renderAttendingCastModal();
        attendingCastModal?.show();
    };

    document.addEventListener('click', (event) => {
        const removeNominationButton = event.target.closest('[data-remove-nomination]');
        if (removeNominationButton) {
            removeNominationButton.closest('[data-nomination-row]')?.remove();
            renumberNominations();
            return;
        }

        const castButton = event.target.closest('[data-open-cast-modal]');
        if (castButton) {
            const row = castButton.closest('[data-nomination-row]');
            if (row) {
                openCastModal(row);
            }
        }
    });

    attendingCastModalElement?.addEventListener('hidden.bs.modal', () => {
        castModalTargetRow = null;
    });

    if (showAddNominationModal) {
        addNominationModal?.show();
    }
})();
