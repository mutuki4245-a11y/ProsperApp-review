(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const castOptions = pageData.castOptions ?? [];
    const orderItems = pageData.orderItems ?? [];
    const initialOrderQueue = pageData.initialOrderQueue ?? [];
    const showOrderModal = pageData.showOrderModal === true;
    const saveStatus = window.TerminalSaveStatus;
    const moneyText = window.MoneyText;
    const formatYen = moneyText.yen;
    const orderQueue = new Map();
    let pendingBackItemId = null;
    let isOrderCorrectionMode = false;

    initialOrderQueue.forEach((line) => {
        if (line.itemId > 0 && line.quantity > 0) {
            const key = makeOrderQueueKey(line.itemId, line.castBackCastId);
            orderQueue.set(key, {
                itemId: String(line.itemId),
                castBackCastId: line.castBackCastId ? String(line.castBackCastId) : null,
                quantity: line.quantity
            });
        }
    });

    function section() {
        return document.getElementById('slipOrdersSection');
    }

    function find(selector) {
        return section()?.querySelector(selector) ?? null;
    }

    function findAll(selector) {
        return Array.from(section()?.querySelectorAll(selector) ?? []);
    }

    function makeOrderQueueKey(itemId, castBackCastId) {
        return `${itemId}:${castBackCastId ?? ''}`;
    }

    const orderLineRows = () => findAll('[data-order-line-row]');

    const recalculateOrderTotal = () => {
        const detailOrderTotal = find('[data-detail-order-total]');
        if (!detailOrderTotal) {
            return;
        }

        const fixedOrderTotal = Number(detailOrderTotal.dataset.detailOrderBaseTotal ?? 0);
        const orderTotal = orderLineRows().reduce((total, row) => {
            const input = row.querySelector('[data-order-quantity-input]');
            const quantity = Number(input?.value ?? row.dataset.orderInitialQuantity ?? 0);
            const unitPrice = Number(row.dataset.orderUnitPrice ?? 0);
            return total + unitPrice * quantity;
        }, fixedOrderTotal);
        detailOrderTotal.textContent = moneyText.amount(orderTotal);
    };

    const updateOrderQuantityState = () => {
        const orderQuantitySave = find('[data-order-quantity-save]');
        const orderQuantityStatus = find('[data-order-quantity-status]');
        const dirty = orderLineRows().some((row) => {
            const input = row.querySelector('[data-order-quantity-input]');
            return input && Number(input.value) !== Number(row.dataset.orderInitialQuantity ?? 0);
        });
        if (orderQuantitySave) {
            orderQuantitySave.disabled = !dirty;
        }
        if (dirty) {
            saveStatus.dirty(orderQuantityStatus);
        } else {
            saveStatus.saved(orderQuantityStatus);
        }
    };

    const setOrderCorrectionMode = (enabled) => {
        const orderCorrectionPanel = find('[data-order-correction-panel]');
        const orderCorrectionToggle = find('[data-order-correction-toggle]');
        const orderQuantitySave = find('[data-order-quantity-save]');
        isOrderCorrectionMode = enabled;
        orderCorrectionPanel?.classList.toggle('is-order-correction', enabled);
        orderCorrectionToggle?.setAttribute('aria-pressed', enabled ? 'true' : 'false');
        if (orderCorrectionToggle) {
            orderCorrectionToggle.textContent = enabled ? '訂正を閉じる' : '訂正モード';
        }
        if (orderQuantitySave) {
            orderQuantitySave.hidden = !enabled;
        }
        findAll('[data-order-quantity-control]').forEach((control) => {
            control.hidden = !enabled;
        });
        findAll('[data-order-quantity-text]').forEach((text) => {
            text.hidden = enabled;
        });
        updateOrderQuantityState();
    };

    const setOrderLineQuantity = (row, quantity) => {
        const normalized = Math.max(0, Math.trunc(Number(quantity) || 0));
        const input = row.querySelector('[data-order-quantity-input]');
        const display = row.querySelector('[data-order-quantity-display]');
        const text = row.querySelector('[data-order-quantity-text]');
        const amount = row.querySelector('[data-order-line-amount]');
        const unitPrice = Number(row.dataset.orderUnitPrice ?? 0);
        if (input) {
            input.value = String(normalized);
        }
        if (display) {
            display.textContent = String(normalized);
        }
        if (text) {
            text.textContent = String(normalized);
        }
        if (amount) {
            amount.textContent = moneyText.amount(unitPrice * normalized);
        }
        recalculateOrderTotal();
        updateOrderQuantityState();
    };

    const addToOrderQueue = (itemId, castBackCastId = null) => {
        const key = makeOrderQueueKey(itemId, castBackCastId);
        const current = orderQueue.get(key) ?? {
            itemId: String(itemId),
            castBackCastId: castBackCastId ? String(castBackCastId) : null,
            quantity: 0
        };
        current.quantity += 1;
        orderQueue.set(key, current);
        renderOrderQueue();
    };

    const closeOrderBackPicker = () => {
        pendingBackItemId = null;
        const modalElement = document.getElementById('orderAttendingCastSelectModal');
        const modal = modalElement ? bootstrap.Modal.getOrCreateInstance(modalElement) : null;
        modal?.hide();
    };

    const renderOrderAttendingCastModal = () => {
        const modalList = document.getElementById('orderAttendingCastModalList');
        window.CastSelectModal.renderOptionalBackTarget(modalList, castOptions, {
            getLabel: (cast) => cast.name,
            onNone: () => {
                if (pendingBackItemId) {
                    addToOrderQueue(pendingBackItemId, null);
                }
                closeOrderBackPicker();
            },
            onSelect: (cast) => {
                if (pendingBackItemId) {
                    addToOrderQueue(pendingBackItemId, cast.id);
                }
                closeOrderBackPicker();
            }
        });
    };

    const openOrderBackPicker = (itemId) => {
        const modalElement = document.getElementById('orderAttendingCastSelectModal');
        const modal = modalElement ? bootstrap.Modal.getOrCreateInstance(modalElement) : null;
        pendingBackItemId = String(itemId);
        renderOrderAttendingCastModal();
        modal?.show();
    };

    function renderOrderQueue() {
        const detailOrderQueueFields = find('#detailOrderQueueFields');
        const detailOrderQueueList = find('#detailOrderQueueList');
        const detailOrderQueueJson = find('#detailOrderQueueJson');
        const detailOrderQueueEmpty = find('#detailOrderQueueEmpty');
        const detailOrderQueueStatus = find('#detailOrderQueueStatus');
        const detailOrderQueueTotal = find('#detailOrderQueueTotal');
        const detailSubmitOrderButton = find('#detailSubmitOrderButton');
        if (detailOrderQueueFields) {
            detailOrderQueueFields.innerHTML = '';
        }
        if (detailOrderQueueList) {
            detailOrderQueueList.innerHTML = '';
        }

        let index = 0;
        let total = 0;
        const serializedLines = [];
        orderQueue.forEach((line, key) => {
            const item = orderItems.find((candidate) => String(candidate.id) === String(line.itemId));
            if (!item) {
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
                itemId: Number(item.id),
                quantity,
                castBackCastId: line.castBackCastId ? Number(line.castBackCastId) : null
            });

            detailOrderQueueFields?.insertAdjacentHTML('beforeend', `
                <input type="hidden" name="QueueLines[${index}].ItemId" value="${item.id}" />
                <input type="hidden" name="QueueLines[${index}].Quantity" value="${quantity}" />
                ${line.castBackCastId ? `<input type="hidden" name="QueueLines[${index}].CastBackCastId" value="${line.castBackCastId}" />` : ''}
            `);

            const row = document.createElement('div');
            row.className = 'order-queue__row';
            const main = document.createElement('div');
            main.className = 'order-queue__row-main';
            const name = document.createElement('strong');
            name.textContent = item.name;
            main.appendChild(name);
            if (cast) {
                const back = document.createElement('small');
                back.className = 'order-queue__back';
                back.textContent = window.OrderBackText.summary(cast, item, quantity);
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
            remove.dataset.detailRemoveOrderItem = key;
            remove.textContent = '削除';
            row.append(main, amount, remove);
            detailOrderQueueList?.appendChild(row);
            index += 1;
        });

        if (detailOrderQueueJson) {
            detailOrderQueueJson.value = JSON.stringify(serializedLines);
        }

        const hasQueue = serializedLines.length > 0;
        if (detailOrderQueueEmpty) {
            detailOrderQueueEmpty.hidden = hasQueue;
        }
        saveStatus.set(detailOrderQueueStatus, hasQueue ? 'dirty' : 'saved', hasQueue ? '未送信' : '空');
        if (detailOrderQueueTotal) {
            detailOrderQueueTotal.textContent = formatYen(total);
        }
        if (detailSubmitOrderButton) {
            detailSubmitOrderButton.disabled = detailSubmitOrderButton.dataset.submitBaseDisabled === 'true' || !hasQueue;
        }
    }

    const handlePartialSubmit = (form) => {
        const isOrderAdd = form.id === 'slipOrderForm';
        const isAdjustmentAdd = form.hasAttribute('data-adjustment-form');
        if (isOrderAdd) {
            renderOrderQueue();
            saveStatus.saving(find('#detailOrderQueueStatus'));
        } else if (form.hasAttribute('data-order-quantity-form')) {
            saveStatus.saving(find('[data-order-quantity-status]'));
        } else if (isAdjustmentAdd) {
            saveStatus.saving(find('[data-adjustment-status]'));
        }

        return window.PartialForms?.submit(form, {
            section: 'slipOrdersSection',
            modalId: isOrderAdd ? 'slipOrderModal' : isAdjustmentAdd ? 'adjustmentModal' : null,
            status: isAdjustmentAdd ? '[data-adjustment-status]' : '[data-order-quantity-status]',
            afterReplace: (_section, result) => {
                if (isOrderAdd && !result.hasErrors) {
                    orderQueue.clear();
                }
                window.SlipAdjustments?.init();
                recalculateOrderTotal();
                setOrderCorrectionMode(false);
                renderOrderQueue();
            }
        });
    };

    section()?.addEventListener('click', (event) => {
        const orderItemButton = event.target.closest('[data-detail-item-id]');
        if (orderItemButton && section()?.contains(orderItemButton)) {
            const itemId = orderItemButton.dataset.detailItemId ?? '';
            const item = orderItems.find((candidate) => String(candidate.id) === String(itemId));
            if (!item || item.isKaraoke) {
                return;
            }

            if (item?.isCastBackTarget) {
                openOrderBackPicker(itemId);
                return;
            }

            addToOrderQueue(itemId);
            return;
        }

        const removeOrderItemButton = event.target.closest('[data-detail-remove-order-item]');
        if (removeOrderItemButton && section()?.contains(removeOrderItemButton)) {
            orderQueue.delete(removeOrderItemButton.dataset.detailRemoveOrderItem ?? '');
            renderOrderQueue();
            return;
        }

        const categoryTab = event.target.closest('[data-detail-category-tab]');
        if (categoryTab && section()?.contains(categoryTab)) {
            const index = categoryTab.dataset.detailCategoryTab ?? '';
            findAll('[data-detail-category-tab]').forEach((tab) => {
                tab.classList.toggle('is-active', tab === categoryTab);
            });
            findAll('[data-detail-category-panel]').forEach((panel) => {
                panel.classList.toggle('is-active', panel.dataset.detailCategoryPanel === index);
            });
            return;
        }

        if (event.target.closest('[data-order-correction-toggle]')) {
            setOrderCorrectionMode(!isOrderCorrectionMode);
            return;
        }

        const orderQuantityDecrement = event.target.closest('[data-order-quantity-decrement]');
        const orderQuantityIncrement = event.target.closest('[data-order-quantity-increment]');
        if (orderQuantityDecrement || orderQuantityIncrement) {
            const row = event.target.closest('[data-order-line-row]');
            const input = row?.querySelector('[data-order-quantity-input]');
            if (!row || !input) {
                return;
            }

            setOrderLineQuantity(row, Number(input.value || 0) + (orderQuantityIncrement ? 1 : -1));
            return;
        }

        if (event.target.closest('#detailClearQueueButton')) {
            orderQueue.clear();
            renderOrderQueue();
        }
    });

    section()?.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-partial-form="orders"]');
        if (!form || !section()?.contains(form)) {
            return;
        }

        event.preventDefault();
        handlePartialSubmit(form);
    });

    document.addEventListener('hidden.bs.modal', (event) => {
        if (event.target?.id === 'orderAttendingCastSelectModal') {
            pendingBackItemId = null;
        }
    });

    if (showOrderModal) {
        const modalElement = document.getElementById('slipOrderModal');
        const modal = modalElement ? bootstrap.Modal.getOrCreateInstance(modalElement) : null;
        modal?.show();
    }

    window.SlipOrders = {
        renderOrderQueue,
        resetOrderCorrection: () => setOrderCorrectionMode(false)
    };

    recalculateOrderTotal();
    setOrderCorrectionMode(false);
    renderOrderQueue();
})();
