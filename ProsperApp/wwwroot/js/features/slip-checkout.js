(() => {
    const dataElement = document.getElementById('slipEditData');
    const pageData = dataElement ? JSON.parse(dataElement.textContent || '{}') : {};
    let showCheckoutModal = pageData.showCheckoutModal === true;
    const formatYen = window.MoneyText.yen;

    const init = () => {
        const section = document.getElementById('slipCheckoutSection') ?? document;
        const slipCheckoutModalElement = document.getElementById('slipCheckoutModal');
        const detailCheckoutClosedTime = document.getElementById('detailCheckoutClosedTime');
        const detailPaymentPanel = section.querySelector('[data-detail-total-amount]');
        const detailPaymentRows = Array.from(section.querySelectorAll('[data-detail-payment-row]'));
        const detailReceivedInput = document.getElementById('detailReceivedAmountInput');
        const detailPaymentSection = document.getElementById('detailPaymentSection');
        const detailCashSection = document.getElementById('detailCashSection');
        const detailCheckoutForm = document.getElementById('detailCheckoutForm');
        const detailReceiptAddresseeInput = section.querySelector('[data-detail-receipt-addressee-input]');
        const detailReceiptAddresseeHidden = section.querySelector('[data-detail-receipt-addressee-hidden]');
        const detailCashPaymentFields = document.getElementById('detailCashPaymentFields');
        const detailBackToPaymentsButton = document.getElementById('detailBackToPaymentsButton');
        const detailCashDisplay = document.getElementById('detailCashAmountDisplay');
        const detailChangeDisplay = document.getElementById('detailChangeAmountDisplay');
        const checkoutCancelForm = section.querySelector('[data-checkout-cancel-form]');

        if (!slipCheckoutModalElement && !checkoutCancelForm) {
            return;
        }

        if (slipCheckoutModalElement?.dataset.checkoutInitialized === 'true' ||
            checkoutCancelForm?.dataset.checkoutInitialized === 'true') {
            return;
        }

        if (slipCheckoutModalElement) {
            slipCheckoutModalElement.dataset.checkoutInitialized = 'true';
        }
        if (checkoutCancelForm) {
            checkoutCancelForm.dataset.checkoutInitialized = 'true';
        }

        const slipCheckoutModal = slipCheckoutModalElement
            ? bootstrap.Modal.getOrCreateInstance(slipCheckoutModalElement)
            : null;
        const detailTotalAmount = Number(detailPaymentPanel?.dataset.detailTotalAmount ?? 0);
        const detailFinalClosedTimeDisplays = Array.from(section.querySelectorAll('[data-detail-final-closed-time]'));
        const detailPaymentSummaryDisplays = Array.from(section.querySelectorAll('[data-detail-payment-summary]'));
        const detailCashSummaryDisplays = Array.from(section.querySelectorAll('[data-detail-cash-summary]'));
        const detailChangeSummaryDisplays = Array.from(section.querySelectorAll('[data-detail-change-summary]'));
        let detailCashAmount = Number(detailCashDisplay?.dataset.cashAmount ?? 0);

        const syncClosedTimeFields = () => {
            section.querySelectorAll('[data-detail-checkout-closed-time-field]').forEach((field) => {
                field.value = detailCheckoutClosedTime?.value ?? field.value;
            });
            detailFinalClosedTimeDisplays.forEach((display) => {
                display.textContent = detailCheckoutClosedTime?.value || '-';
            });
        };

        const syncReceiptAddresseeFields = () => {
            if (detailReceiptAddresseeHidden && detailReceiptAddresseeInput) {
                detailReceiptAddresseeHidden.value = detailReceiptAddresseeInput.value;
            }
        };

        const selectedPaymentRows = () => detailPaymentRows.filter((row) => row.querySelector('[data-detail-payment-selected]')?.checked);
        const getPaymentAmountInput = (row) => row.querySelector('[data-detail-payment-amount]');
        const getPaymentMethodCode = (row) => row.querySelector('[data-detail-payment-method-code]')?.value ?? '';
        const getCashPaymentRow = () => detailPaymentRows.find((row) => getPaymentMethodCode(row) === 'cash') ?? null;

        const selectedPayments = () => selectedPaymentRows().map((row) => {
            const methodCode = getPaymentMethodCode(row);
            const methodName = row.querySelector('[data-detail-payment-method-name]')?.value ?? '';
            const amount = getPaymentAmountInput(row)?.value ?? '0';
            return { methodCode, methodName, amount };
        });

        const selectedNonCashTotal = () => selectedPaymentRows()
            .filter((row) => getPaymentMethodCode(row) !== 'cash')
            .reduce((total, row) => total + Number(getPaymentAmountInput(row)?.value || 0), 0);

        const syncCashAmountToRemainder = () => {
            const cashRow = getCashPaymentRow();
            const cashSelected = cashRow?.querySelector('[data-detail-payment-selected]')?.checked === true;
            const cashAmount = cashRow ? getPaymentAmountInput(cashRow) : null;
            if (!cashSelected || !cashAmount) {
                return;
            }

            const remainder = Math.max(detailTotalAmount - selectedNonCashTotal(), 0);
            cashAmount.value = String(remainder);
            if (detailTotalAmount > 0 && remainder === 0) {
                const cashCheckbox = cashRow.querySelector('[data-detail-payment-selected]');
                if (cashCheckbox) {
                    cashCheckbox.checked = false;
                }
            }
        };

        const applySingleSelectedDefault = (row) => {
            const rows = selectedPaymentRows();
            if (rows.length !== 1 || rows[0] !== row) {
                return;
            }

            const amount = getPaymentAmountInput(row);
            if (amount && Number(amount.value || 0) === 0) {
                amount.value = String(detailTotalAmount);
            }
        };

        const refreshChange = () => {
            if (!detailReceivedInput || !detailChangeDisplay) {
                return;
            }

            const received = Number(detailReceivedInput.value || 0);
            if (received <= 0) {
                detailChangeDisplay.textContent = '-';
                detailChangeSummaryDisplays.forEach((display) => {
                    display.textContent = '-';
                });
                return;
            }

            const change = formatYen(Math.max(received - detailCashAmount, 0));
            detailChangeDisplay.textContent = change;
            detailChangeSummaryDisplays.forEach((display) => {
                display.textContent = change;
            });
        };

        const refreshPaymentRows = () => {
            detailPaymentRows.forEach((row) => {
                const checkbox = row.querySelector('[data-detail-payment-selected]');
                const button = row.querySelector('[data-detail-payment-toggle]');
                const amount = getPaymentAmountInput(row);
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

            const summary = selectedPayments()
                .filter((payment) => Number(payment.amount || 0) > 0)
                .map((payment) => `${payment.methodName} ${formatYen(Number(payment.amount || 0))}`)
                .join(' / ');
            detailPaymentSummaryDisplays.forEach((display) => {
                display.textContent = summary || '-';
            });

            const cashAmount = selectedPayments()
                .filter((payment) => payment.methodCode === 'cash')
                .reduce((total, payment) => total + Number(payment.amount || 0), 0);
            detailCashAmount = cashAmount;
            detailCashSummaryDisplays.forEach((display) => {
                display.textContent = formatYen(cashAmount);
            });
            if (detailCashDisplay) {
                detailCashDisplay.dataset.cashAmount = String(cashAmount);
                detailCashDisplay.textContent = formatYen(cashAmount);
            }
            refreshChange();
        };

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
            const payments = selectedPayments();
            syncReceiptAddresseeFields();
            detailCashAmount = payments
                .filter((payment) => payment.methodCode === 'cash')
                .reduce((total, payment) => total + Number(payment.amount || 0), 0);
            populateCashPaymentFields(payments);
            if (detailCashDisplay) {
                detailCashDisplay.dataset.cashAmount = String(detailCashAmount);
                detailCashDisplay.textContent = formatYen(detailCashAmount);
            }
            detailCashSummaryDisplays.forEach((display) => {
                display.textContent = formatYen(detailCashAmount);
            });
            if (detailPaymentSection) {
                detailPaymentSection.hidden = true;
            }
            if (detailCashSection) {
                detailCashSection.hidden = false;
            }
            detailReceivedInput?.focus();
            refreshChange();
        };

        const showPaymentStep = () => {
            if (detailCashSection) {
                detailCashSection.hidden = true;
            }
            if (detailPaymentSection) {
                detailPaymentSection.hidden = false;
            }
        };

        checkoutCancelForm?.addEventListener('submit', (event) => {
            const confirmed = window.confirm('確定済み会計を訂正します。この操作は売上、残金、印刷済み領収書などに影響します');
            if (!confirmed) {
                event.preventDefault();
            }
        });

        detailCheckoutClosedTime?.addEventListener('change', syncClosedTimeFields);
        syncClosedTimeFields();
        detailReceiptAddresseeInput?.addEventListener('input', syncReceiptAddresseeFields);
        syncReceiptAddresseeFields();

        detailPaymentRows.forEach((row) => {
            const checkbox = row.querySelector('[data-detail-payment-selected]');
            row.querySelector('[data-detail-payment-toggle]')?.addEventListener('click', () => {
                const wasSelected = checkbox?.checked === true;
                if (checkbox) {
                    checkbox.checked = !wasSelected;
                }
                if (!wasSelected) {
                    applySingleSelectedDefault(row);
                }
                syncCashAmountToRemainder();
                refreshPaymentRows();
            });
            getPaymentAmountInput(row)?.addEventListener('input', () => {
                if (getPaymentMethodCode(row) !== 'cash') {
                    syncCashAmountToRemainder();
                }
                refreshPaymentRows();
            });
        });
        refreshPaymentRows();

        detailCheckoutForm?.addEventListener('submit', (event) => {
            syncClosedTimeFields();
            syncReceiptAddresseeFields();
            const payments = selectedPayments();
            const hasCashPayment = payments.some((payment) => payment.methodCode === 'cash' && Number(payment.amount || 0) > 0);
            if (!hasCashPayment) {
                return;
            }

            event.preventDefault();
            showCashStep();
        });

        detailBackToPaymentsButton?.addEventListener('click', showPaymentStep);
        detailReceivedInput?.addEventListener('input', refreshChange);
        refreshChange();

        if (showCheckoutModal) {
            showCheckoutModal = false;
            slipCheckoutModal?.show();
        }
    };

    window.SlipCheckout = { init };
    init();
})();
