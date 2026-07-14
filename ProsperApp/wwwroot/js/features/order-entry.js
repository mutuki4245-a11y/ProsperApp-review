(() => {
    const dataElement = document.getElementById('orderEntryData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    let slips = pageData.slips ?? [];
    const items = pageData.items ?? [];
    const castOptions = pageData.castOptions ?? [];
    const initialQueue = pageData.initialQueue ?? [];
    const initialSlipId = pageData.initialSlipId;
    const slipOptionsUrl = pageData.slipOptionsUrl || '';
    const refreshIntervalMs = pageData.refreshIntervalMs || 10000;

    const selectedSlipInput = document.getElementById('selectedSlipId');
    const orderQueueJson = document.getElementById('orderQueueJson');
    const orderForm = document.getElementById('orderForm');
    const selectedSlipBadge = document.getElementById('selectedSlipBadge');
    const selectedSlipQueueLabel = document.getElementById('selectedSlipQueueLabel');
    const slipPicker = document.querySelector('[data-order-slip-picker]');
    const slipOptionsWarning = document.getElementById('slipOptionsWarning');
    const queueFields = document.getElementById('queueFields');
    const queueList = document.getElementById('queueList');
    const queueEmpty = document.getElementById('queueEmpty');
    const queueTotal = document.getElementById('queueTotal');
    const submitOrderButton = document.getElementById('submitOrderButton');
    const clearQueueButton = document.getElementById('clearQueueButton');
    const queueStatus = document.getElementById('queueStatus');
    const queue = new Map();
    const submitBaseDisabled = submitOrderButton?.disabled ?? false;
    let pendingBackItemId = null;
    let slipOptionsLoaded = slips.length > 0;
    let slipOptionsLoading = false;
    const attendingCastModalElement = document.getElementById('attendingCastSelectModal');
    const attendingCastModalList = document.getElementById('attendingCastModalList');
    const attendingCastModal = attendingCastModalElement ? new bootstrap.Modal(attendingCastModalElement) : null;

    initialQueue.forEach((line) => {
        if (line.slipId > 0 && line.itemId > 0 && line.quantity > 0) {
            const key = makeQueueKey(line.slipId, line.itemId, line.castBackCastId);
            queue.set(key, {
                slipId: String(line.slipId),
                itemId: String(line.itemId),
                castBackCastId: line.castBackCastId ? String(line.castBackCastId) : null,
                quantity: line.quantity
            });
        }
    });

    function makeQueueKey(slipId, itemId, castBackCastId) {
        return `${slipId}:${itemId}:${castBackCastId ?? ''}`;
    }

    const formatYen = (value) => `${Math.round(value).toLocaleString('ja-JP')} 円`;
    const hasSlip = (slipId) => slips.some((slip) => String(slip.id) === String(slipId));
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

    const renderSlipPicker = () => {
        if (!slipPicker) {
            return;
        }

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

                const name = document.createElement('strong');
                name.dataset.orderSlipName = '';
                const meta = document.createElement('span');
                meta.dataset.orderSlipMeta = '';
                button.append(name, meta);
            }

            button.classList.toggle('is-selected', String(selectedSlipInput?.value ?? '') === slipId);
            setText(button.querySelector('[data-order-slip-name]'), slip.display);
            setText(button.querySelector('[data-order-slip-meta]'), `${slip.openedTime} 入店 / ${slip.customerCount} 人`);

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
            return;
        }

        let removed = false;
        if (selectedSlipInput?.value && !hasSlip(selectedSlipInput.value)) {
            selectedSlipInput.value = '';
            removed = true;
        }

        queue.forEach((line, key) => {
            if (!hasSlip(line.slipId)) {
                queue.delete(key);
                removed = true;
            }
        });

        if (removed) {
            setSlipWarning('会計済みまたは利用できなくなった卓番をキューから外しました。');
        }
    };

    const loadSlipOptions = async () => {
        if (slipOptionsLoading || !slipOptionsUrl) {
            return;
        }

        slipOptionsLoading = true;
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
            slips = Array.isArray(result.slips) ? result.slips : [];
            slipOptionsLoaded = true;
            removeUnavailableSlipLines();
            render();
        } catch {
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
        }
    };

    const openBackPicker = (itemId) => {
        pendingBackItemId = String(itemId);
        renderAttendingCastModal();
        attendingCastModal?.show();
    };

    const closeBackPicker = () => {
        pendingBackItemId = null;
        attendingCastModal?.hide();
    };

    const addToQueue = (itemId, castBackCastId = null) => {
        const slipId = selectedSlipInput?.value ?? '';
        if (!slipId || !hasSlip(slipId)) {
            return;
        }

        const key = makeQueueKey(slipId, itemId, castBackCastId);
        const current = queue.get(key) ?? {
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
        const selectedSlip = slips.find((slip) => String(slip.id) === String(selectedSlipInput?.value ?? ''));
        if (selectedSlipBadge) {
            selectedSlipBadge.textContent = selectedSlip ? selectedSlip.display : '卓番未選択';
        }
        if (selectedSlipQueueLabel) {
            selectedSlipQueueLabel.textContent = selectedSlip ? selectedSlip.display : '卓番未選択';
        }

        renderSlipPicker();

        if (queueFields) {
            queueFields.innerHTML = '';
        }
        if (queueList) {
            queueList.innerHTML = '';
        }

        let index = 0;
        let total = 0;
        const serializedLines = [];
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
            serializedLines.push({
                slipId: Number(lineSlip.id),
                itemId: Number(item.id),
                quantity,
                castBackCastId: line.castBackCastId ? Number(line.castBackCastId) : null
            });

            if (queueFields) {
                queueFields.insertAdjacentHTML('beforeend', `
                    <input type="hidden" name="QueueLines[${index}].ItemId" value="${item.id}" />
                    <input type="hidden" name="QueueLines[${index}].SlipId" value="${lineSlip.id}" />
                    <input type="hidden" name="QueueLines[${index}].Quantity" value="${quantity}" />
                    ${line.castBackCastId ? `<input type="hidden" name="QueueLines[${index}].CastBackCastId" value="${line.castBackCastId}" />` : ''}
                `);
            }

            const row = document.createElement('div');
            row.className = 'order-queue__row';
            const main = document.createElement('div');
            main.className = 'order-queue__row-main';
            const name = document.createElement('strong');
            name.textContent = item.name;
            const slip = document.createElement('small');
            slip.className = 'order-queue__back';
            slip.textContent = lineSlip.display;
            main.append(name, slip);
            if (cast) {
                const back = document.createElement('small');
                back.className = 'order-queue__back';
                back.textContent = `${cast.display} / ドリンク ${formatYen(Number(item.castBackRegularUnitAmount) * quantity)} / 担当 ${formatYen(Number(item.castBackNominationUnitAmount) * quantity)}`;
                main.appendChild(back);
            }

            const amount = document.createElement('div');
            amount.className = 'order-queue__row-amount';
            const price = document.createElement('span');
            price.textContent = `${formatYen(Number(item.price))} x ${quantity}`;
            const subtotalText = document.createElement('strong');
            subtotalText.textContent = formatYen(subtotal);
            amount.append(price, subtotalText);

            const remove = document.createElement('button');
            remove.className = 'btn btn-outline-danger btn-sm';
            remove.type = 'button';
            remove.dataset.removeItem = key;
            remove.textContent = '削除';
            row.append(main, amount, remove);
            queueList?.appendChild(row);
            index += 1;
        });

        if (orderQueueJson) {
            orderQueueJson.value = JSON.stringify(serializedLines);
        }

        const hasQueue = serializedLines.length > 0;
        if (queueEmpty) {
            queueEmpty.hidden = hasQueue;
        }
        window.TerminalSaveStatus.set(queueStatus, hasQueue ? 'dirty' : 'saved', hasQueue ? '未送信' : '空');
        if (queueTotal) {
            queueTotal.textContent = formatYen(total);
        }
        if (submitOrderButton) {
            submitOrderButton.disabled = submitBaseDisabled || !hasQueue;
        }
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
        if (!button) {
            return;
        }

        queue.delete(button.dataset.removeItem ?? '');
        render();
    });

    clearQueueButton?.addEventListener('click', () => {
        queue.clear();
        render();
    });

    orderForm?.addEventListener('submit', () => {
        render();
        window.TerminalSaveStatus.saving(queueStatus);
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

    const renderAttendingCastModal = () => {
        window.CastSelectModal.renderOptionalBackTarget(attendingCastModalList, castOptions, {
            getLabel: (cast) => cast.name,
            onNone: () => {
                if (pendingBackItemId) {
                    addToQueue(pendingBackItemId, null);
                }
                closeBackPicker();
            },
            onSelect: (cast) => {
                if (pendingBackItemId) {
                    addToQueue(pendingBackItemId, cast.id);
                }
                closeBackPicker();
            }
        });
    };

    attendingCastModalElement?.addEventListener('hidden.bs.modal', () => {
        pendingBackItemId = null;
    });

    if (selectedSlipInput && initialSlipId) {
        selectedSlipInput.value = initialSlipId;
    }

    renderSlipPicker();
    render();
    void loadSlipOptions();
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
