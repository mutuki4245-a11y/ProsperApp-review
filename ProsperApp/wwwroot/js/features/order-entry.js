(() => {
    const dataElement = document.getElementById('orderEntryData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    let slips = pageData.slips ?? [];
    const items = pageData.items ?? [];
    let castOptions = pageData.castOptions ?? [];
    const initialQueue = pageData.initialQueue ?? [];
    const initialSlipId = pageData.initialSlipId;
    const slipOptionsUrl = pageData.slipOptionsUrl || '';
    const submitUrl = pageData.submitUrl || '';
    const refreshIntervalMs = pageData.refreshIntervalMs || 10000;

    const selectedSlipInput = document.getElementById('selectedSlipId');
    const orderForm = document.getElementById('orderForm');
    const selectedSlipBadge = document.getElementById('selectedSlipBadge');
    const selectedSlipQueueLabel = document.getElementById('selectedSlipQueueLabel');
    const slipPicker = document.querySelector('[data-order-slip-picker]');
    const slipOptionsWarning = document.getElementById('slipOptionsWarning');
    const queueList = document.getElementById('queueList');
    const queueEmpty = document.getElementById('queueEmpty');
    const queueTotal = document.getElementById('queueTotal');
    const submitOrderButton = document.getElementById('submitOrderButton');
    const clearQueueButton = document.getElementById('clearQueueButton');
    const queueStatus = document.getElementById('queueStatus');
    const slipCountBadge = document.getElementById('orderSlipCountBadge');
    const refreshSlipOptionsButton = document.getElementById('refreshSlipOptionsButton');
    const itemSelectionWarning = document.getElementById('orderItemSelectionWarning');
    const businessDayWarning = document.getElementById('orderBusinessDayWarning');
    const queue = new Map();
    const submitBaseDisabled = submitOrderButton?.disabled ?? false;
    let currentBusinessDayId = pageData.businessDayId || null;
    let currentBusinessDayRevision = pageData.businessDayRevision ?? null;
    let draftStorageKey = pageData.departmentId && currentBusinessDayId
        ? `prosper:order-entry-draft:v1:${pageData.departmentId}:${pageData.businessDayId}`
        : '';
    let restoredDraftKey = '';
    let pendingSubmitCommand = null;
    let pendingBackItemId = null;
    let pendingBackCastSelection = new Map();
    let slipOptionsLoaded = slips.length > 0;
    let slipOptionsLoading = false;
    const attendingCastModalElement = document.getElementById('attendingCastSelectModal');
    const attendingCastModalList = document.getElementById('attendingCastModalList');
    const attendingCastConfirmButton = document.getElementById('attendingCastConfirmButton');
    const attendingCastModal = attendingCastModalElement ? new bootstrap.Modal(attendingCastModalElement) : null;

    initialQueue.forEach((line) => {
        if (line.slipId > 0 && line.itemId > 0 && line.quantity > 0) {
            const key = makeQueueKey(line.slipId, line.itemId, line.castBackCastId);
            queue.set(key, {
                clientLineId: line.clientLineId || crypto.randomUUID(),
                slipId: String(line.slipId),
                itemId: String(line.itemId),
                castBackCastId: line.castBackCastId ? String(line.castBackCastId) : null,
                quantity: line.quantity
            });
        }
    });

    const safeJsonParse = (value) => {
        try {
            return JSON.parse(value || 'null');
        } catch {
            return null;
        }
    };

    const readStoredDraft = () => {
        if (!draftStorageKey) {
            return null;
        }

        let draft = null;
        try {
            draft = safeJsonParse(localStorage.getItem(draftStorageKey));
        } catch {
            return null;
        }
        if (!draft || !Array.isArray(draft.lines)) {
            return null;
        }

        return draft;
    };

    const clearStoredDraft = () => {
        try {
            if (draftStorageKey) {
                localStorage.removeItem(draftStorageKey);
            }
        } catch {
            // 端末設定でlocalStorageが無効な場合は、画面内キューだけで続行する。
        }
    };

    const persistDraft = () => {
        if (!draftStorageKey) {
            return;
        }

        const lines = Array.from(queue.values())
            .map((line) => ({
                slipId: Number(line.slipId),
                clientLineId: line.clientLineId || crypto.randomUUID(),
                itemId: Number(line.itemId),
                castBackCastId: line.castBackCastId ? Number(line.castBackCastId) : null,
                quantity: Math.max(0, Math.trunc(Number(line.quantity) || 0))
            }))
            .filter((line) => line.slipId > 0 && line.itemId > 0 && line.quantity > 0);

        if (lines.length === 0 && !pendingSubmitCommand) {
            clearStoredDraft();
            return;
        }

        try {
            localStorage.setItem(draftStorageKey, JSON.stringify({
                version: 1,
                departmentId: pageData.departmentId,
                businessDayId: currentBusinessDayId,
                selectedSlipId: selectedSlipInput?.value || null,
                pendingSubmitCommand,
                lines
            }));
        } catch {
            // 保存できなくても注文入力自体は継続する。
        }
    };

    const hydrateQueueLines = (lines) => {
        (Array.isArray(lines) ? lines : []).forEach((line) => {
            if (line.slipId > 0 && line.itemId > 0 && line.quantity > 0) {
                const key = makeQueueKey(line.slipId, line.itemId, line.castBackCastId);
                queue.set(key, {
                    clientLineId: line.clientLineId || crypto.randomUUID(),
                    slipId: String(line.slipId),
                    itemId: String(line.itemId),
                    castBackCastId: line.castBackCastId ? String(line.castBackCastId) : null,
                    quantity: Math.max(1, Math.trunc(Number(line.quantity) || 0))
                });
            }
        });
    };

    const restoreStoredDraft = () => {
        if (pageData.discardStoredQueue) {
            clearStoredDraft();
            return;
        }

        if (!draftStorageKey || restoredDraftKey === draftStorageKey) {
            return;
        }
        restoredDraftKey = draftStorageKey;

        if (queue.size > 0) {
            return;
        }

        const draft = readStoredDraft();
        if (!draft) {
            return;
        }

        hydrateQueueLines(draft.lines);
        if (draft.pendingSubmitCommand?.operationId &&
            Number(draft.pendingSubmitCommand.expectedBusinessDayId) === Number(currentBusinessDayId)) {
            pendingSubmitCommand = draft.pendingSubmitCommand;
        }
        if (selectedSlipInput && !selectedSlipInput.value && draft.selectedSlipId) {
            selectedSlipInput.value = String(draft.selectedSlipId);
        }
    };

    function makeQueueKey(slipId, itemId, castBackCastId) {
        return `${slipId}:${itemId}:${castBackCastId ?? ''}`;
    }

    const formatYen = window.MoneyText.yen;
    const hasSlip = (slipId) => slips.some((slip) => String(slip.id) === String(slipId));
    const selectedSlip = () => slips.find((slip) => String(slip.id) === String(selectedSlipInput?.value ?? '')) ?? null;
    const selectedNominationCastIds = () => new Set((selectedSlip()?.nominationCastIds ?? []).map((id) => String(id)));
    const setText = (element, text) => {
        const value = String(text);
        if (element && element.textContent !== value) {
            element.textContent = value;
        }
    };

    const setSlipWarning = (message) => {
        if (!slipOptionsWarning) {
            return;
        }

        slipOptionsWarning.textContent = message ?? '';
        slipOptionsWarning.hidden = !message;
    };

    const getBackTargetCasts = () => {
        const nominationCastIds = selectedNominationCastIds();
        return [...castOptions].sort((left, right) => {
            const leftNominated = nominationCastIds.has(String(left.id));
            const rightNominated = nominationCastIds.has(String(right.id));
            if (leftNominated !== rightNominated) {
                return leftNominated ? -1 : 1;
            }

            return String(left.name ?? left.display ?? '').localeCompare(String(right.name ?? right.display ?? ''), 'ja');
        });
    };

    const renderSlipPicker = () => {
        if (!slipPicker) {
            return;
        }

        setText(slipCountBadge, slipOptionsLoaded ? `${slips.length} 件` : '取得中');

        const removeSlipButtons = () => {
            slipPicker.querySelectorAll('[data-slip-id]').forEach((button) => button.remove());
        };

        slipPicker.querySelector('[data-order-slip-error]')?.remove();

        if (!slipOptionsLoaded) {
            removeSlipButtons();
            slipPicker.querySelector('[data-order-slip-empty]')?.remove();
            let loading = slipPicker.querySelector('[data-order-slip-loading]');
            if (!loading) {
                loading = document.createElement('div');
                loading.className = 'alert alert-secondary mb-0';
                loading.dataset.orderSlipLoading = '';
                slipPicker.appendChild(loading);
            }
            setText(loading, '注文対象の卓番を読み込み中です。');
            return;
        }

        slipPicker.querySelector('[data-order-slip-loading]')?.remove();

        if (slips.length === 0) {
            removeSlipButtons();
            let empty = slipPicker.querySelector('[data-order-slip-empty]');
            if (!empty) {
                empty = document.createElement('div');
                empty.className = 'alert alert-secondary mb-0';
                empty.dataset.orderSlipEmpty = '';
                slipPicker.appendChild(empty);
            }
            setText(empty, '注文登録できるopen伝票がありません。');
            return;
        }

        slipPicker.querySelector('[data-order-slip-empty]')?.remove();

        const buttonsBySlipId = new Map(
            Array.from(slipPicker.querySelectorAll('[data-slip-id]'))
                .map((button) => [button.dataset.slipId, button])
        );
        const renderedSlipIds = new Set();

        slips.forEach((slip, index) => {
            const slipId = String(slip.id);
            let button = buttonsBySlipId.get(slipId);
            if (!button) {
                button = document.createElement('button');
                button.className = 'order-slip-picker__button';
                button.type = 'button';
                button.dataset.slipId = slipId;

                const table = document.createElement('span');
                table.className = 'order-slip-picker__table';
                table.dataset.orderSlipTable = '';
                const info = document.createElement('span');
                info.className = 'order-slip-picker__info';
                const customer = document.createElement('strong');
                customer.dataset.orderSlipCustomer = '';
                const nomination = document.createElement('span');
                nomination.dataset.orderSlipNomination = '';
                info.append(customer, nomination);
                button.append(table, info);
            }

            button.classList.toggle('is-selected', String(selectedSlipInput?.value ?? '') === slipId);
            setText(button.querySelector('[data-order-slip-table]'), slip.display);
            setText(button.querySelector('[data-order-slip-customer]'), slip.customerNames || `${slip.customerCount} 人`);
            setText(button.querySelector('[data-order-slip-nomination]'), slip.nominationCastNames || '指名なし');

            renderedSlipIds.add(slipId);
            const current = slipPicker.children[index] ?? null;
            if (current !== button) {
                slipPicker.insertBefore(button, current);
            }
        });

        buttonsBySlipId.forEach((button, slipId) => {
            if (!renderedSlipIds.has(slipId)) {
                button.remove();
            }
        });
    };

    const removeUnavailableSlipLines = () => {
        if (!slipOptionsLoaded) {
            return false;
        }

        let removed = false;
        if (selectedSlipInput?.value && !hasSlip(selectedSlipInput.value)) {
            selectedSlipInput.value = '';
            removed = true;
        }

        queue.forEach((line) => {
            if (!hasSlip(line.slipId)) {
                removed = true;
            }
        });

        if (removed) {
            setSlipWarning('会計済みまたは利用できなくなった卓番があります。注文内容を残したまま卓番を選び直してください。');
            persistDraft();
        }

        return removed;
    };

    const applyRecoveryCandidates = (candidates) => {
        if (!candidates || typeof candidates !== 'object') return;
        const businessDay = candidates.business_day || candidates.businessDay || null;
        currentBusinessDayId = businessDay?.business_day_id ?? businessDay?.businessDayId ?? null;
        currentBusinessDayRevision = businessDay?.business_ui_revision ?? businessDay?.businessUiRevision ?? null;
        slips = Array.isArray(candidates.slips) ? candidates.slips.map((slip) => ({
            id: slip.slip_id ?? slip.id,
            display: slip.table_name || slip.table_code || slip.display,
            openedTime: slip.opened_at || slip.openedTime,
            customerCount: slip.customer_count ?? slip.customerCount,
            customerNames: slip.customer_names ?? slip.customerNames,
            nominationCastIds: slip.nomination_cast_ids ?? slip.nominationCastIds,
            nominationCastNames: slip.nomination_cast_names ?? slip.nominationCastNames,
            memo: slip.memo
        })) : [];
        castOptions = Array.isArray(candidates.attendance_casts)
            ? candidates.attendance_casts.map((cast) => ({
                id: cast.cast_id,
                name: cast.display_name,
                display: cast.search_display_name || cast.display_name,
                drinkMemo: cast.drink_memo,
                department: cast.department_name
            }))
            : castOptions;
        slipOptionsLoaded = true;
        if (businessDayWarning) businessDayWarning.hidden = Boolean(currentBusinessDayId);
        removeUnavailableSlipLines();
        render();
    };

    const loadSlipOptions = async (manual = false) => {
        if (slipOptionsLoading || !slipOptionsUrl) {
            return;
        }

        slipOptionsLoading = true;
        refreshSlipOptionsButton && (refreshSlipOptionsButton.disabled = true);
        if (manual) {
            setSlipWarning('最新の卓番を取得しています。');
        }
        try {
            const response = await fetch(slipOptionsUrl, {
                headers: {
                    'Accept': 'application/json'
                }
            });
            if (!response.ok) {
                throw new Error('Slip options load failed.');
            }

            const result = await response.json();
            currentBusinessDayId = result.businessDay?.businessDayId || null;
            currentBusinessDayRevision = result.businessDay?.businessUiRevision ?? null;
            if (pageData.departmentId && currentBusinessDayId) {
                draftStorageKey = `prosper:order-entry-draft:v1:${pageData.departmentId}:${currentBusinessDayId}`;
                restoreStoredDraft();
            }
            if (businessDayWarning) businessDayWarning.hidden = Boolean(currentBusinessDayId);
            slips = Array.isArray(result.slips) ? result.slips : [];
            castOptions = Array.isArray(result.attendanceCasts) ? result.attendanceCasts : [];
            slipOptionsLoaded = true;
            const removedUnavailableLines = removeUnavailableSlipLines();
            if (!removedUnavailableLines) {
                setSlipWarning(null);
            }
            render();
        } catch {
            setSlipWarning(slipOptionsLoaded
                ? '注文対象の卓番を取得できませんでした。前回の一覧を表示しています。'
                : '注文対象の卓番を取得できませんでした。');
            if (!slipOptionsLoaded && slipPicker) {
                slipPicker.querySelectorAll('[data-slip-id]').forEach((button) => button.remove());
                slipPicker.querySelector('[data-order-slip-loading]')?.remove();
                slipPicker.querySelector('[data-order-slip-empty]')?.remove();
                let error = slipPicker.querySelector('[data-order-slip-error]');
                if (!error) {
                    error = document.createElement('div');
                    error.className = 'alert alert-secondary mb-0';
                    error.dataset.orderSlipError = '';
                    slipPicker.appendChild(error);
                }
                setText(error, '注文対象の卓番を取得できませんでした。');
            }
        } finally {
            slipOptionsLoading = false;
            refreshSlipOptionsButton && (refreshSlipOptionsButton.disabled = false);
            if (slipOptionsLoaded) {
                renderSlipPicker();
            } else {
                setText(slipCountBadge, '未取得');
            }
        }
    };

    const openBackPicker = (itemId) => {
        pendingBackItemId = String(itemId);
        pendingBackCastSelection = new Map();
        renderAttendingCastModal();
        attendingCastModal?.show();
    };

    const closeBackPicker = () => {
        pendingBackItemId = null;
        pendingBackCastSelection = new Map();
        attendingCastModal?.hide();
    };

    const addToQueue = (itemId, castBackCastId = null) => {
        if (pendingSubmitCommand) {
            setSlipWarning('前回の送信結果を確認するまで注文内容は変更できません。');
            return;
        }
        const slipId = selectedSlipInput?.value ?? '';
        if (!slipId || !hasSlip(slipId)) {
            return;
        }

        const key = makeQueueKey(slipId, itemId, castBackCastId);
        const current = queue.get(key) ?? {
            clientLineId: crypto.randomUUID(),
            slipId: String(slipId),
            itemId: String(itemId),
            castBackCastId: castBackCastId ? String(castBackCastId) : null,
            quantity: 0
        };
        current.quantity += 1;
        queue.set(key, current);
        render();
    };

    const render = () => {
        const currentSlip = selectedSlip();
        if (selectedSlipBadge) {
            selectedSlipBadge.textContent = currentSlip ? currentSlip.display : '卓番未選択';
        }
        if (selectedSlipQueueLabel) {
            selectedSlipQueueLabel.textContent = currentSlip ? currentSlip.display : '卓番未選択';
        }

        renderSlipPicker();

        const hasSelectedSlip = Boolean(currentSlip);
        if (itemSelectionWarning) {
            itemSelectionWarning.hidden = hasSelectedSlip;
        }
        document.querySelectorAll('[data-item-id]').forEach((button) => {
            const disabled = !hasSelectedSlip || Boolean(pendingSubmitCommand);
            button.classList.toggle('is-disabled', disabled);
            button.setAttribute('aria-disabled', disabled ? 'true' : 'false');
        });

        if (queueList) {
            queueList.innerHTML = '';
        }

        let total = 0;
        const renderedLines = [];
        queue.forEach((line, key) => {
            const item = items.find((candidate) => String(candidate.id) === String(line.itemId));
            const lineSlip = slips.find((candidate) => String(candidate.id) === String(line.slipId));
            if (!item || !lineSlip) {
                return;
            }

            const quantity = Math.max(0, Math.trunc(Number(line.quantity) || 0));
            if (quantity <= 0) {
                return;
            }

            const subtotal = Number(item.price) * quantity;
            total += subtotal;
            const cast = line.castBackCastId
                ? castOptions.find((candidate) => String(candidate.id) === String(line.castBackCastId))
                : null;
            renderedLines.push({ key, item, lineSlip, quantity, cast, subtotal });
        });

        const queueGroups = new Map();
        renderedLines.forEach((line) => {
            const slipId = String(line.lineSlip.id);
            let group = queueGroups.get(slipId);
            if (!group) {
                group = {
                    slip: line.lineSlip,
                    lines: [],
                    quantity: 0,
                    total: 0
                };
                queueGroups.set(slipId, group);
            }

            group.lines.push(line);
            group.quantity += line.quantity;
            group.total += line.subtotal;
        });

        const buildQueueRow = (line) => {
            const row = document.createElement('div');
            row.className = 'order-queue__row';
            const name = document.createElement('strong');
            name.className = 'order-queue__row-name';
            name.textContent = line.item.name;

            const amount = document.createElement('strong');
            amount.className = 'order-queue__row-price';
            amount.textContent = formatYen(line.subtotal);

            const cast = document.createElement('span');
            cast.className = 'order-queue__row-cast';
            if (line.cast) {
                cast.textContent = line.cast.name || line.cast.display || '';
            } else {
                cast.textContent = '指定なし';
            }

            const remove = document.createElement('button');
            remove.className = 'btn btn-outline-danger btn-sm';
            remove.type = 'button';
            remove.dataset.removeItem = line.key;
            remove.textContent = '削除';
            remove.disabled = Boolean(pendingSubmitCommand);
            row.append(name, amount, cast, remove);
            return row;
        };

        queueGroups.forEach((group) => {
            const groupElement = document.createElement('section');
            groupElement.className = 'order-queue__group';

            const header = document.createElement('div');
            header.className = 'order-queue__group-header';
            const title = document.createElement('strong');
            title.textContent = group.slip.display;
            header.appendChild(title);
            groupElement.appendChild(header);

            group.lines.forEach((line) => {
                groupElement.appendChild(buildQueueRow(line));
            });

            queueList?.appendChild(groupElement);
        });

        const hasQueue = renderedLines.length > 0;
        if (queueEmpty) {
            queueEmpty.hidden = hasQueue;
            if (!hasQueue) {
                queueEmpty.textContent = currentSlip ? '商品を選択してください。' : '先に卓番を選択してください。';
            }
        }
        window.TerminalSaveStatus.set(queueStatus, hasQueue ? 'dirty' : 'saved', hasQueue ? '未送信' : '空');
        if (queueTotal) {
            queueTotal.textContent = formatYen(total);
        }
        if (submitOrderButton) {
            submitOrderButton.disabled = submitBaseDisabled || (!hasQueue && !pendingSubmitCommand);
        }
        if (clearQueueButton) {
            clearQueueButton.disabled = !hasQueue || Boolean(pendingSubmitCommand);
        }
        persistDraft();
    };

    slipPicker?.addEventListener('click', (event) => {
        const button = event.target.closest('[data-slip-id]');
        if (!button || !slipPicker.contains(button)) {
            return;
        }

        if (selectedSlipInput) {
            selectedSlipInput.value = button.dataset.slipId ?? '';
        }
        setSlipWarning(null);
        render();
    });

    document.querySelectorAll('[data-item-id]').forEach((button) => {
        button.addEventListener('click', () => {
            if (!selectedSlipInput?.value) {
                if (itemSelectionWarning) {
                    itemSelectionWarning.hidden = false;
                    itemSelectionWarning.focus?.();
                }
                return;
            }

            const itemId = button.dataset.itemId ?? '';
            const item = items.find((candidate) => String(candidate.id) === String(itemId));
            if (!item || item.isKaraoke) {
                return;
            }

            if (item?.isCastBackTarget) {
                openBackPicker(itemId);
                return;
            }

            addToQueue(itemId);
        });
    });

    queueList?.addEventListener('click', (event) => {
        const button = event.target.closest('[data-remove-item]');
        if (!button || pendingSubmitCommand) {
            return;
        }

        queue.delete(button.dataset.removeItem ?? '');
        render();
    });

    clearQueueButton?.addEventListener('click', () => {
        if (pendingSubmitCommand) {
            return;
        }
        queue.clear();
        render();
    });

    orderForm?.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (!currentBusinessDayId || currentBusinessDayRevision === null ||
            (!pendingSubmitCommand && queue.size === 0) || !submitUrl) {
            setSlipWarning('営業日と注文内容を確認してください。');
            return;
        }

        pendingSubmitCommand ??= {
            operationId: crypto.randomUUID(),
            expectedBusinessDayId: Number(currentBusinessDayId),
            expectedBusinessDayRevision: Number(currentBusinessDayRevision),
            lines: Array.from(queue.values()).map((line) => ({
                clientLineId: line.clientLineId || crypto.randomUUID(),
                slipId: Number(line.slipId),
                itemId: Number(line.itemId),
                quantity: Math.max(1, Math.trunc(Number(line.quantity) || 0)),
                castBackCastId: line.castBackCastId ? Number(line.castBackCastId) : null
            }))
        };
        persistDraft();

        render();
        window.TerminalSaveStatus.saving(queueStatus);
        submitOrderButton && (submitOrderButton.disabled = true);
        try {
            const token = orderForm.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
            const response = await fetch(submitUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/json',
                    ...(token ? { RequestVerificationToken: token } : {})
                },
                body: JSON.stringify(pendingSubmitCommand)
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || result?.status !== 'confirmed') {
                if (result?.recoveryCandidates) {
                    applyRecoveryCandidates(result.recoveryCandidates);
                }
                if (['conflict', 'validation_error'].includes(result?.status)) {
                    pendingSubmitCommand = null;
                    persistDraft();
                }
                throw new Error(result?.message || '注文を登録できませんでした。');
            }

            const confirmedIds = new Set((result.insertedLines || []).map((line) => String(line.client_line_id || line.clientLineId || '')));
            Array.from(queue.entries()).forEach(([key, line]) => {
                if (confirmedIds.has(String(line.clientLineId))) queue.delete(key);
            });
            pendingSubmitCommand = null;
            persistDraft();
            currentBusinessDayId = result.businessDayId ?? currentBusinessDayId;
            currentBusinessDayRevision = result.businessDayRevision ?? currentBusinessDayRevision;
            window.TerminalSaveStatus.saved(queueStatus);
            setSlipWarning(null);
            render();
        } catch (error) {
            window.TerminalSaveStatus.error(queueStatus, error?.message || '注文を登録できませんでした。');
            setSlipWarning(error?.message || '注文を登録できませんでした。');
        } finally {
            submitOrderButton && (submitOrderButton.disabled = queue.size === 0);
        }
    });

    document.querySelectorAll('[data-category-tab]').forEach((button) => {
        button.addEventListener('click', () => {
            const index = button.dataset.categoryTab ?? '';
            document.querySelectorAll('[data-category-tab]').forEach((tab) => {
                tab.classList.toggle('is-active', tab === button);
            });
            document.querySelectorAll('[data-category-panel]').forEach((panel) => {
                panel.classList.toggle('is-active', panel.dataset.categoryPanel === index);
            });
        });
    });

    const updateBackPickerConfirm = () => {
        if (attendingCastConfirmButton) {
            const totalCount = Array.from(pendingBackCastSelection.values())
                .reduce((total, count) => total + count, 0);
            attendingCastConfirmButton.disabled = !pendingBackItemId || totalCount <= 0;
        }
    };

    const changeBackSelectionCount = (castId, delta) => {
        const key = String(castId ?? '');
        const current = pendingBackCastSelection.get(key) ?? 0;
        const next = Math.max(0, current + delta);
        if (next > 0) {
            pendingBackCastSelection.set(key, next);
        } else {
            pendingBackCastSelection.delete(key);
        }
        renderAttendingCastModal();
    };

    const renderBackTargetRow = (cast, nominationCastIds) => {
        const row = document.createElement('div');
        row.className = 'cast-select-modal__item cast-select-modal__item--stepper';
        row.dataset.castBackTarget = String(cast.id);
        const isNominated = nominationCastIds.has(String(cast.id));
        const count = pendingBackCastSelection.get(String(cast.id)) ?? 0;
        row.classList.toggle('is-nominated', isNominated);

        const label = document.createElement('div');
        label.className = 'cast-select-modal__item-label';

        const name = document.createElement('strong');
        const drinkMemo = typeof cast.drinkMemo === 'string'
            ? cast.drinkMemo.replace(/\s+/g, ' ').trim()
            : '';
        const displayName = cast.name || cast.display || '';
        name.textContent = drinkMemo ? `${displayName}（${drinkMemo}）` : displayName;
        label.appendChild(name);

        if (isNominated) {
            const badge = document.createElement('span');
            badge.textContent = '指名';
            label.appendChild(badge);
        }

        const controls = document.createElement('div');
        controls.className = 'cast-select-modal__stepper';

        const decrement = document.createElement('button');
        decrement.type = 'button';
        decrement.className = 'btn btn-outline-secondary btn-sm';
        decrement.textContent = '-';
        decrement.disabled = count <= 0;
        decrement.addEventListener('click', () => changeBackSelectionCount(cast.id, -1));

        const countDisplay = document.createElement('strong');
        countDisplay.textContent = String(count);
        countDisplay.setAttribute('aria-label', `${name.textContent} ${count}件`);

        const increment = document.createElement('button');
        increment.type = 'button';
        increment.className = 'btn btn-outline-primary btn-sm';
        increment.textContent = '+';
        increment.addEventListener('click', () => changeBackSelectionCount(cast.id, 1));

        controls.append(decrement, countDisplay, increment);
        row.append(label, controls);
        return row;
    };

    const renderNoneBackTargetRow = () => {
        const row = document.createElement('div');
        row.className = 'cast-select-modal__item cast-select-modal__item--none cast-select-modal__item--stepper';
        const count = pendingBackCastSelection.get('') ?? 0;

        const label = document.createElement('div');
        label.className = 'cast-select-modal__item-label';

        const name = document.createElement('strong');
        name.textContent = '指定なし';
        label.appendChild(name);

        const controls = document.createElement('div');
        controls.className = 'cast-select-modal__stepper';

        const decrement = document.createElement('button');
        decrement.type = 'button';
        decrement.className = 'btn btn-outline-secondary btn-sm';
        decrement.textContent = '-';
        decrement.disabled = count <= 0;
        decrement.addEventListener('click', () => changeBackSelectionCount('', -1));

        const countDisplay = document.createElement('strong');
        countDisplay.textContent = String(count);
        countDisplay.setAttribute('aria-label', `指定なし ${count}件`);

        const increment = document.createElement('button');
        increment.type = 'button';
        increment.className = 'btn btn-outline-primary btn-sm';
        increment.textContent = '+';
        increment.addEventListener('click', () => changeBackSelectionCount('', 1));

        controls.append(decrement, countDisplay, increment);
        row.append(label, controls);
        return row;
    };

    const renderAttendingCastModal = () => {
        if (!attendingCastModalList) {
            return;
        }

        const nominationCastIds = selectedNominationCastIds();
        attendingCastModalList.innerHTML = '';
        attendingCastModalList.appendChild(renderNoneBackTargetRow());

        const casts = getBackTargetCasts();
        if (casts.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'text-muted mb-0';
            empty.textContent = '出勤キャストが登録されていません。';
            attendingCastModalList.appendChild(empty);
            updateBackPickerConfirm();
            return;
        }

        casts.forEach((cast) => {
            attendingCastModalList.appendChild(renderBackTargetRow(cast, nominationCastIds));
        });
        updateBackPickerConfirm();
    };

    attendingCastConfirmButton?.addEventListener('click', () => {
        if (!pendingBackItemId || pendingBackCastSelection.size === 0) {
            return;
        }

        pendingBackCastSelection.forEach((count, castId) => {
            for (let i = 0; i < count; i += 1) {
                addToQueue(pendingBackItemId, castId ? castId : null);
            }
        });
        closeBackPicker();
    });

    attendingCastModalElement?.addEventListener('hidden.bs.modal', () => {
        pendingBackItemId = null;
        pendingBackCastSelection = new Map();
        updateBackPickerConfirm();
    });

    if (selectedSlipInput && initialSlipId) {
        selectedSlipInput.value = initialSlipId;
    }
    restoreStoredDraft();

    renderSlipPicker();
    render();
    void loadSlipOptions();
    refreshSlipOptionsButton?.addEventListener('click', () => {
        void loadSlipOptions(true);
    });
    window.setInterval(() => {
        if (document.visibilityState === 'visible') {
            void loadSlipOptions();
        }
    }, refreshIntervalMs);
    window.addEventListener('focus', () => {
        if (document.visibilityState === 'visible') {
            void loadSlipOptions();
        }
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            void loadSlipOptions();
        }
    });
})();
