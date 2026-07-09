(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const castOptions = pageData.castOptions ?? [];
    const orderItems = pageData.orderItems ?? [];
    const initialOrderQueue = pageData.initialOrderQueue ?? [];
    const showOrderModal = pageData.showOrderModal === true;
    const showAdjustmentModal = pageData.showAdjustmentModal === true;
    const showCheckoutModal = pageData.showCheckoutModal === true;
    const showAddCustomerModal = pageData.showAddCustomerModal === true;
    const showAddNominationModal = pageData.showAddNominationModal === true;
    const slipId = pageData.slipId ? String(pageData.slipId) : '';
    const businessDayId = pageData.businessDayId ? String(pageData.businessDayId) : '';
    const draftPrefix = `prosper:slip:${businessDayId}:${slipId}`;

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

    const setPartialStatus = (sectionId, message) => {
        const section = document.getElementById(sectionId);
        const status = section?.querySelector('[data-partial-status]');
        setSaveStatus(status, message);
    };

    const parseValidation = (root) => {
        if (window.jQuery?.validator?.unobtrusive) {
            window.jQuery.validator.unobtrusive.parse(root);
        }
    };

    const replacePartial = async (form, sectionId) => {
        const section = document.getElementById(sectionId);
        if (!section) {
            form.submit();
            return;
        }

        setPartialStatus(sectionId, '保存中');
        const response = await fetch(form.action, {
            method: 'POST',
            body: new FormData(form),
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        });

        const html = await response.text();
        section.innerHTML = html;
        parseValidation(section);
        setPartialStatus(sectionId, '保存済み');
        window.AppLoading?.hide(form);
    };

    const getCustomerList = () => document.getElementById('customerList');

    const renumberRows = () => {
        const list = getCustomerList();
        if (!list) {
            return;
        }

        const customerNumberStart = Number.parseInt(list.dataset.customerNumberStart ?? '1', 10) || 1;
        list.querySelectorAll('[data-customer-row]').forEach((row, index) => {
            const label = row.querySelector('.customer-row__index');
            if (label) {
                label.textContent = String(customerNumberStart + index);
            }
            const removeButton = row.querySelector('[data-remove-customer]');
            if (removeButton) {
                removeButton.hidden = index === 0;
            }
        });
    };

    const removeCustomerRow = (button) => {
        const list = getCustomerList();
        if (!list) {
            return;
        }

        const rows = list.querySelectorAll('[data-customer-row]');
        if (rows.length <= 1) {
            return;
        }

        button.closest('[data-customer-row]')?.remove();
        renumberRows();
    };

    const attendingCastModalElement = document.getElementById('attendingCastSelectModal');
    const attendingCastModalList = document.getElementById('attendingCastModalList');
    const attendingCastModal = attendingCastModalElement ? new bootstrap.Modal(attendingCastModalElement) : null;
    let castModalTargetRow = null;
    const addCustomerModalElement = document.getElementById('addCustomerModal');
    const addCustomerModal = addCustomerModalElement ? new bootstrap.Modal(addCustomerModalElement) : null;
    const addNominationModalElement = document.getElementById('addNominationModal');
    const addNominationModal = addNominationModalElement ? new bootstrap.Modal(addNominationModalElement) : null;
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
    const slipCheckoutModalElement = document.getElementById('slipCheckoutModal');
    const slipCheckoutModal = slipCheckoutModalElement ? new bootstrap.Modal(slipCheckoutModalElement) : null;
    const detailCheckoutClosedTime = document.getElementById('detailCheckoutClosedTime');
    const detailPaymentPanel = document.querySelector('[data-detail-total-amount]');
    const detailTotalAmount = Number(detailPaymentPanel?.dataset.detailTotalAmount ?? 0);
    const detailPaymentRows = Array.from(document.querySelectorAll('[data-detail-payment-row]'));
    const detailReceivedInput = document.getElementById('detailReceivedAmountInput');
    const detailPaymentSection = document.getElementById('detailPaymentSection');
    const detailCashSection = document.getElementById('detailCashSection');
    const detailCheckoutForm = document.getElementById('detailCheckoutForm');
    const detailCashPaymentFields = document.getElementById('detailCashPaymentFields');
    const detailBackToPaymentsButton = document.getElementById('detailBackToPaymentsButton');
    const detailCashDisplay = document.getElementById('detailCashAmountDisplay');
    const detailChangeDisplay = document.getElementById('detailChangeAmountDisplay');
    const detailOrderTotal = document.querySelector('[data-detail-order-total]');
    const orderCorrectionPanel = document.querySelector('[data-order-correction-panel]');
    const orderCorrectionToggle = document.querySelector('[data-order-correction-toggle]');
    const orderQuantityForm = document.querySelector('[data-order-quantity-form]');
    const orderQuantitySave = document.querySelector('[data-order-quantity-save]');
    const orderQuantityStatus = document.querySelector('[data-order-quantity-status]');
    const adjustmentModalElement = document.getElementById('adjustmentModal');
    const adjustmentModal = adjustmentModalElement ? new bootstrap.Modal(adjustmentModalElement) : null;
    const adjustmentForm = document.querySelector('[data-adjustment-form]');
    const adjustmentList = document.querySelector('[data-adjustment-list]');
    const addAdjustmentButton = document.querySelector('[data-add-adjustment-row]');
    const adjustmentStatus = document.querySelector('[data-adjustment-status]');
    let detailCashAmount = Number(detailCashDisplay?.dataset.cashAmount ?? 0);
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
    const adjustmentDraftKey = `${draftPrefix}:adjustments`;
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

    const readAdjustmentRows = () => Array.from(adjustmentList?.querySelectorAll('[data-adjustment-row]') ?? [])
        .map((row) => ({
            lineName: row.querySelector('[data-adjustment-name]')?.value ?? '',
            amount: row.querySelector('[data-adjustment-amount]')?.value ?? ''
        }));

    const saveAdjustmentDraft = () => {
        if (!slipId || !adjustmentList) {
            return;
        }

        localStorage.setItem(adjustmentDraftKey, JSON.stringify(readAdjustmentRows()));
        setSaveStatus(adjustmentStatus, '未保存');
    };

    const renumberAdjustmentRows = () => {
        adjustmentList?.querySelectorAll('[data-adjustment-row]').forEach((row, index) => {
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

    const appendAdjustmentRow = (lineName = '', amount = '') => {
        if (!adjustmentList) {
            return;
        }

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
        renumberAdjustmentRows();
    };

    const restoreDetailDrafts = () => {
        if (adjustmentList && slipId) {
            const draft = localStorage.getItem(adjustmentDraftKey);
            if (draft) {
                try {
                    const rows = JSON.parse(draft);
                    if (Array.isArray(rows)) {
                        adjustmentList.innerHTML = '';
                        rows.forEach((row) => appendAdjustmentRow(row.lineName ?? '', row.amount ?? ''));
                        if (rows.length === 0) {
                            appendAdjustmentRow();
                        }
                        setSaveStatus(adjustmentStatus, '未保存');
                    }
                } catch {
                    localStorage.removeItem(adjustmentDraftKey);
                }
            }
        }
        renumberAdjustmentRows();
    };

    const syncCheckoutClosedTimeFields = () => {
        document.querySelectorAll('[data-detail-checkout-closed-time-field]').forEach((field) => {
            field.value = detailCheckoutClosedTime?.value ?? field.value;
        });
    };

    const selectedDetailPaymentRows = () => detailPaymentRows.filter((row) => row.querySelector('[data-detail-payment-selected]')?.checked);

    const refreshDetailPaymentRows = () => {
        const rows = selectedDetailPaymentRows();
        detailPaymentRows.forEach((row) => {
            const checkbox = row.querySelector('[data-detail-payment-selected]');
            const button = row.querySelector('[data-detail-payment-toggle]');
            const amount = row.querySelector('[data-detail-payment-amount]');
            const isSelected = checkbox?.checked === true;
            button?.classList.toggle('btn-primary', isSelected);
            button?.classList.toggle('btn-outline-primary', !isSelected);
            if (amount) {
                amount.disabled = !isSelected;
                if (!isSelected) {
                    amount.value = '0';
                }
            }
        });

        if (rows.length === 1) {
            const amount = rows[0].querySelector('[data-detail-payment-amount]');
            if (amount && Number(amount.value || 0) === 0) {
                amount.value = String(detailTotalAmount);
            }
        }
    };

    const refreshDetailChange = () => {
        if (!detailReceivedInput || !detailChangeDisplay) {
            return;
        }

        const received = Number(detailReceivedInput.value || 0);
        if (received <= 0) {
            detailChangeDisplay.textContent = '-';
            return;
        }

        detailChangeDisplay.textContent = `${Math.max(received - detailCashAmount, 0).toLocaleString('ja-JP')} 円`;
    };

    const selectedCheckoutPayments = () => selectedDetailPaymentRows().map((row) => {
        const methodCode = row.querySelector('[data-detail-payment-method-code]')?.value ?? '';
        const methodName = row.querySelector('[data-detail-payment-method-name]')?.value ?? '';
        const amount = row.querySelector('[data-detail-payment-amount]')?.value ?? '0';
        return { methodCode, methodName, amount };
    });

    const populateCashPaymentFields = (payments) => {
        if (!detailCashPaymentFields) {
            return;
        }

        detailCashPaymentFields.innerHTML = '';
        payments.forEach((payment, index) => {
            detailCashPaymentFields.insertAdjacentHTML('beforeend', `
                <input type="hidden" name="CheckoutInput.Payments[${index}].MethodCode" value="${payment.methodCode}" />
                <input type="hidden" name="CheckoutInput.Payments[${index}].MethodName" value="${payment.methodName}" />
                <input type="hidden" name="CheckoutInput.Payments[${index}].IsSelected" value="true" />
                <input type="hidden" name="CheckoutInput.Payments[${index}].Amount" value="${payment.amount}" />
            `);
        });
    };

    const showCashStep = () => {
        const payments = selectedCheckoutPayments();
        detailCashAmount = payments
            .filter((payment) => payment.methodCode === 'cash')
            .reduce((total, payment) => total + Number(payment.amount || 0), 0);
        populateCashPaymentFields(payments);
        if (detailCashDisplay) {
            detailCashDisplay.dataset.cashAmount = String(detailCashAmount);
            detailCashDisplay.textContent = formatYen(detailCashAmount);
        }
        if (detailPaymentSection) {
            detailPaymentSection.hidden = true;
        }
        if (detailCashSection) {
            detailCashSection.hidden = false;
        }
        detailReceivedInput?.focus();
        refreshDetailChange();
    };

    const showPaymentStep = () => {
        if (detailCashSection) {
            detailCashSection.hidden = true;
        }
        if (detailPaymentSection) {
            detailPaymentSection.hidden = false;
        }
    };

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
        const removeCustomerButton = event.target.closest('[data-remove-customer]');
        if (removeCustomerButton) {
            removeCustomerRow(removeCustomerButton);
            return;
        }

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

        if (event.target.closest('[data-add-adjustment-row]')) {
            appendAdjustmentRow();
            saveAdjustmentDraft();
            return;
        }

        const removeAdjustmentButton = event.target.closest('[data-remove-adjustment-row]');
        if (removeAdjustmentButton) {
            const rows = adjustmentList?.querySelectorAll('[data-adjustment-row]') ?? [];
            if (rows.length <= 1) {
                const row = removeAdjustmentButton.closest('[data-adjustment-row]');
                const name = row?.querySelector('[data-adjustment-name]');
                const amount = row?.querySelector('[data-adjustment-amount]');
                if (name) {
                    name.value = '';
                }
                if (amount) {
                    amount.value = '0';
                }
            } else {
                removeAdjustmentButton.closest('[data-adjustment-row]')?.remove();
            }
            renumberAdjustmentRows();
            saveAdjustmentDraft();
        }
    });

    adjustmentList?.addEventListener('input', saveAdjustmentDraft);

    orderQuantityForm?.addEventListener('submit', () => {
        setSaveStatus(orderQuantityStatus, '保存中');
    });

    adjustmentForm?.addEventListener('submit', () => {
        setSaveStatus(adjustmentStatus, '保存中');
        renumberAdjustmentRows();
        localStorage.removeItem(adjustmentDraftKey);
    });

    detailClearQueueButton?.addEventListener('click', () => {
        orderQueue.clear();
        renderOrderQueue();
    });

    slipOrderForm?.addEventListener('submit', () => {
        renderOrderQueue();
        setSaveStatus(detailOrderQueueStatus, '保存中');
    });

    detailCheckoutClosedTime?.addEventListener('change', syncCheckoutClosedTimeFields);
    syncCheckoutClosedTimeFields();

    detailPaymentRows.forEach((row) => {
        const checkbox = row.querySelector('[data-detail-payment-selected]');
        row.querySelector('[data-detail-payment-toggle]')?.addEventListener('click', () => {
            if (checkbox) {
                checkbox.checked = !checkbox.checked;
            }
            refreshDetailPaymentRows();
        });
    });
    refreshDetailPaymentRows();

    detailCheckoutForm?.addEventListener('submit', (event) => {
        syncCheckoutClosedTimeFields();
        const payments = selectedCheckoutPayments();
        const hasCashPayment = payments.some((payment) => payment.methodCode === 'cash' && Number(payment.amount || 0) > 0);
        if (!hasCashPayment) {
            return;
        }

        event.preventDefault();
        showCashStep();
    });

    detailBackToPaymentsButton?.addEventListener('click', showPaymentStep);
    detailReceivedInput?.addEventListener('input', refreshDetailChange);
    refreshDetailChange();

    document.querySelector('[data-checkout-cancel-form]')?.addEventListener('submit', (event) => {
        const confirmed = window.confirm('確定済み会計を訂正します。この操作は売上、残金、印刷済み領収書などに影響します');
        if (!confirmed) {
            event.preventDefault();
        }
    });

    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-partial-form]');
        if (!form) {
            return;
        }

        event.preventDefault();
        const sectionId = form.dataset.partialForm === 'customers'
            ? 'slipCustomersSection'
            : 'slipNominationsSection';
        replacePartial(form, sectionId).catch(() => form.submit());
    });

    attendingCastModalElement?.addEventListener('hidden.bs.modal', () => {
        castModalTargetRow = null;
    });

    orderAttendingCastModalElement?.addEventListener('hidden.bs.modal', () => {
        pendingBackItemId = null;
    });

    if (showOrderModal) {
        slipOrderModal?.show();
    }

    if (showAdjustmentModal) {
        adjustmentModal?.show();
    }

    if (showCheckoutModal) {
        slipCheckoutModal?.show();
    }

    if (showAddCustomerModal) {
        addCustomerModal?.show();
    }

    if (showAddNominationModal) {
        addNominationModal?.show();
    }

    restoreDetailDrafts();
    recalculateOrderTotal();
    setOrderCorrectionMode(false);
    renderOrderQueue();
})();
