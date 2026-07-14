(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const castOptions = pageData.castOptions ?? [];
    const orderItems = pageData.orderItems ?? [];
    const initialOrderQueue = pageData.initialOrderQueue ?? [];
    const showOrderModal = pageData.showOrderModal === true;
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

    const slipOrderModalElement = document.getElementById('slipOrderModal');
    const slipOrderModal = slipOrderModalElement ? new bootstrap.Modal(slipOrderModalElement) : null;
    const slipOrderForm = document.getElementById('slipOrderForm');
    const orderAttendingCastModalElement = document.getElementById('orderAttendingCastSelectModal');
    const orderAttendingCastModalList = document.getElementById('orderAttendingCastModalList');
    const orderAttendingCastModal = orderAttendingCastModalElement ? new bootstrap.Modal(orderAttendingCastModalElement) : null;
    const detailOrderQueueJson = document.getElementById('detailOrderQueueJson');
    const detailOrderQueueFields = document.getElementById('detailOrderQueueFields');
    const detailOrderQueueList = document.getElementById('detailOrderQueueList');
    const detailOrderQueueEmpty = document.getElementById('detailOrderQueueEmpty');
    const detailOrderQueueTotal = document.getElementById('detailOrderQueueTotal');
    const detailOrderQueueStatus = document.getElementById('detailOrderQueueStatus');
    const detailSubmitOrderButton = document.getElementById('detailSubmitOrderButton');
    const detailClearQueueButton = document.getElementById('detailClearQueueButton');
    const detailOrderTotal = document.querySelector('[data-detail-order-total]');
    const orderCorrectionPanel = document.querySelector('[data-order-correction-panel]');
    const orderCorrectionToggle = document.querySelector('[data-order-correction-toggle]');
    const orderQuantityForm = document.querySelector('[data-order-quantity-form]');
    const orderQuantitySave = document.querySelector('[data-order-quantity-save]');
    const orderQuantityStatus = document.querySelector('[data-order-quantity-status]');
    const orderQueue = new Map();
    const submitOrderBaseDisabled = detailSubmitOrderButton?.disabled ?? false;
    let pendingBackItemId = null;

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

    function makeOrderQueueKey(itemId, castBackCastId) {
        return `${itemId}:${castBackCastId ?? ''}`;
    }

    const formatYen = (value) => `${Math.round(value).toLocaleString('ja-JP')} 円`;
    let isOrderCorrectionMode = false;

    const orderLineRows = () => Array.from(document.querySelectorAll('[data-order-line-row]'));

    const recalculateOrderTotal = () => {
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
        detailOrderTotal.textContent = formatYen(orderTotal).replace(' 円', '');
    };

    const updateOrderQuantityState = () => {
        const dirty = orderLineRows().some((row) => {
            const input = row.querySelector('[data-order-quantity-input]');
            return input && Number(input.value) !== Number(row.dataset.orderInitialQuantity ?? 0);
        });
        if (orderQuantitySave) {
            orderQuantitySave.disabled = !dirty;
        }
        setSaveStatus(orderQuantityStatus, dirty ? '未保存' : '保存済み');
    };

    const setOrderCorrectionMode = (enabled) => {
        isOrderCorrectionMode = enabled;
        orderCorrectionPanel?.classList.toggle('is-order-correction', enabled);
        orderCorrectionToggle?.setAttribute('aria-pressed', enabled ? 'true' : 'false');
        if (orderCorrectionToggle) {
            orderCorrectionToggle.textContent = enabled ? '訂正を閉じる' : '訂正モード';
        }
        if (orderQuantitySave) {
            orderQuantitySave.hidden = !enabled;
        }
        document.querySelectorAll('[data-order-quantity-control]').forEach((control) => {
            control.hidden = !enabled;
        });
        document.querySelectorAll('[data-order-quantity-text]').forEach((text) => {
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
            amount.textContent = Math.round(unitPrice * normalized).toLocaleString('ja-JP');
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
        orderAttendingCastModal?.hide();
    };

    const renderOrderAttendingCastModal = () => {
        if (!orderAttendingCastModalList) {
            return;
        }

        orderAttendingCastModalList.innerHTML = '';
        const matches = castOptions.slice(0, 80);

        const noBack = document.createElement('button');
        noBack.type = 'button';
        noBack.className = 'cast-select-modal__item';
        const noBackName = document.createElement('strong');
        noBackName.textContent = '指定なし';
        const noBackHelp = document.createElement('span');
        noBackHelp.textContent = 'バックの摘要を付けずに登録';
        noBack.append(noBackName, noBackHelp);
        noBack.addEventListener('click', () => {
            if (pendingBackItemId) {
                addToOrderQueue(pendingBackItemId, null);
            }
            closeOrderBackPicker();
        });
        orderAttendingCastModalList.appendChild(noBack);

        matches.forEach((cast) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'cast-select-modal__item';
            const name = document.createElement('strong');
            name.textContent = cast.name;
            button.append(name);
            button.addEventListener('click', () => {
                if (pendingBackItemId) {
                    addToOrderQueue(pendingBackItemId, cast.id);
                }
                closeOrderBackPicker();
            });
            orderAttendingCastModalList.appendChild(button);
        });
    };

    const openOrderBackPicker = (itemId) => {
        pendingBackItemId = String(itemId);
        renderOrderAttendingCastModal();
        orderAttendingCastModal?.show();
    };

    const renderOrderQueue = () => {
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
            const name = document.createElement('strong');
            name.textContent = item.name;
            const price = document.createElement('span');
            price.textContent = formatYen(Number(item.price));
            const quantityText = document.createElement('span');
            quantityText.textContent = String(quantity);
            const subtotalText = document.createElement('span');
            subtotalText.textContent = formatYen(subtotal);
            const remove = document.createElement('button');
            remove.className = 'btn btn-outline-danger btn-sm';
            remove.type = 'button';
            remove.dataset.detailRemoveOrderItem = key;
            remove.textContent = '削除';
            row.append(name, price, quantityText, subtotalText, remove);
            if (cast) {
                const back = document.createElement('small');
                back.className = 'order-queue__back';
                back.textContent = `${cast.display} / ドリンク ${formatYen(Number(item.castBackRegularUnitAmount) * quantity)} / 担当 ${formatYen(Number(item.castBackNominationUnitAmount) * quantity)}`;
                row.appendChild(back);
            }
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
        if (detailOrderQueueStatus) {
            detailOrderQueueStatus.textContent = hasQueue ? '未送信' : '空';
            detailOrderQueueStatus.dataset.saveState = hasQueue ? 'dirty' : 'saved';
        }
        if (detailOrderQueueTotal) {
            detailOrderQueueTotal.textContent = formatYen(total);
        }
        if (detailSubmitOrderButton) {
            detailSubmitOrderButton.disabled = submitOrderBaseDisabled || !hasQueue;
        }
    };

    document.addEventListener('click', (event) => {
        const orderItemButton = event.target.closest('[data-detail-item-id]');
        if (orderItemButton) {
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
        if (removeOrderItemButton) {
            orderQueue.delete(removeOrderItemButton.dataset.detailRemoveOrderItem ?? '');
            renderOrderQueue();
            return;
        }

        const categoryTab = event.target.closest('[data-detail-category-tab]');
        if (categoryTab) {
            const index = categoryTab.dataset.detailCategoryTab ?? '';
            document.querySelectorAll('[data-detail-category-tab]').forEach((tab) => {
                tab.classList.toggle('is-active', tab === categoryTab);
            });
            document.querySelectorAll('[data-detail-category-panel]').forEach((panel) => {
                panel.classList.toggle('is-active', panel.dataset.detailCategoryPanel === index);
            });
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

    });

    orderQuantityForm?.addEventListener('submit', () => {
        setSaveStatus(orderQuantityStatus, '保存中');
    });

    detailClearQueueButton?.addEventListener('click', () => {
        orderQueue.clear();
        renderOrderQueue();
    });

    slipOrderForm?.addEventListener('submit', () => {
        renderOrderQueue();
        setSaveStatus(detailOrderQueueStatus, '保存中');
    });

    orderAttendingCastModalElement?.addEventListener('hidden.bs.modal', () => {
        pendingBackItemId = null;
    });

    if (showOrderModal) {
        slipOrderModal?.show();
    }

    recalculateOrderTotal();
    setOrderCorrectionMode(false);
    renderOrderQueue();
})();
