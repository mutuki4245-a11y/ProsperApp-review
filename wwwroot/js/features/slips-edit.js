(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    const castOptions = pageData.castOptions ?? [];
    const orderItems = pageData.orderItems ?? [];
    const initialOrderQueue = pageData.initialOrderQueue ?? [];
    const showOrderModal = pageData.showOrderModal === true;
    const showCheckoutModal = pageData.showCheckoutModal === true;
    const showAddCustomerModal = pageData.showAddCustomerModal === true;
    const showAddNominationModal = pageData.showAddNominationModal === true;

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
    const orderAttendingCastModalElement = document.getElementById('orderAttendingCastSelectModal');
    const orderAttendingCastModalList = document.getElementById('orderAttendingCastModalList');
    const orderAttendingCastModal = orderAttendingCastModalElement ? new bootstrap.Modal(orderAttendingCastModalElement) : null;
    const detailOrderQueueFields = document.getElementById('detailOrderQueueFields');
    const detailOrderQueueList = document.getElementById('detailOrderQueueList');
    const detailOrderQueueEmpty = document.getElementById('detailOrderQueueEmpty');
    const detailOrderQueueTotal = document.getElementById('detailOrderQueueTotal');
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
            const castId = row.querySelector('[data-cast-id]');
            const castName = row.querySelector('[data-cast-name-hidden]');
            const removeButton = row.querySelector('[data-remove-nomination]');
            if (label) {
                label.textContent = String(index + 1);
            }
            if (kind) {
                kind.name = 'AddNominationsInput.CastNominations[0].NominationKind';
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
        hiddenName.value = cast.display;
        selected.textContent = cast.display;
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
            name.textContent = cast.name;
            const department = document.createElement('span');
            department.textContent = cast.department ?? '';
            button.append(name, department);
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
        if (matches.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'text-muted mb-0';
            empty.textContent = '出勤キャストが登録されていません。';
            orderAttendingCastModalList.appendChild(empty);
            return;
        }

        matches.forEach((cast) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'cast-select-modal__item';
            const name = document.createElement('strong');
            name.textContent = cast.name;
            const department = document.createElement('span');
            department.textContent = cast.department ?? '';
            button.append(name, department);
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
        orderQueue.forEach((line, key) => {
            const item = orderItems.find((candidate) => String(candidate.id) === String(line.itemId));
            if (!item) {
                return;
            }

            const quantity = line.quantity;
            const subtotal = Number(item.price) * quantity;
            total += subtotal;
            const cast = line.castBackCastId
                ? castOptions.find((candidate) => String(candidate.id) === String(line.castBackCastId))
                : null;

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
                back.textContent = `${cast.display} / 通常 ${formatYen(Number(item.castBackRegularUnitAmount) * quantity)} / 指名 ${formatYen(Number(item.castBackNominationUnitAmount) * quantity)}`;
                row.appendChild(back);
            }
            detailOrderQueueList?.appendChild(row);
            index += 1;
        });

        const hasQueue = orderQueue.size > 0;
        if (detailOrderQueueEmpty) {
            detailOrderQueueEmpty.hidden = hasQueue;
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
            if (item?.isCastBackTarget) {
                if (castOptions.length === 0) {
                    return;
                }
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
    });

    detailClearQueueButton?.addEventListener('click', () => {
        orderQueue.clear();
        renderOrderQueue();
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

    if (showCheckoutModal) {
        slipCheckoutModal?.show();
    }

    if (showAddCustomerModal) {
        addCustomerModal?.show();
    }

    if (showAddNominationModal) {
        addNominationModal?.show();
    }

    renderOrderQueue();
})();
