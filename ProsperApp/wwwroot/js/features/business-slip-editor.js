(() => {
    const config = window.prosperBusinessHome ?? {};
    const editorUrl = config.businessSlipEditorUrl || '';
    const modalElement = document.querySelector('[data-business-slip-editor-modal]');
    const content = modalElement?.querySelector('[data-business-slip-editor-content]');
    const title = modalElement?.querySelector('[data-business-slip-editor-title]');
    const table = modalElement?.querySelector('[data-business-slip-editor-table]');
    const labels = {
        customers: '客を編集',
        nominations: '指名を編集',
        adjustments: '自由明細を編集'
    };
    const state = {
        section: null,
        slipId: null,
        requestId: 0,
        isSubmitting: false
    };

    if (!editorUrl || !modalElement || !content || !window.bootstrap?.Modal) {
        return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const currentSlip = (slipId) => (window.prosperBusinessHomeSlips || []).find((slip) => String(slip.id) === String(slipId));

    const setLoading = () => {
        content.innerHTML = '<div class="slip-create__panel slip-detail-panel-loading">編集内容を読み込み中です。</div>';
    };

    const setError = (message) => {
        content.replaceChildren();
        const alert = document.createElement('div');
        alert.className = 'alert alert-warning mb-0';
        alert.setAttribute('role', 'alert');
        alert.textContent = message;
        content.appendChild(alert);
    };

    const buildUrl = (section, slipId) => {
        const url = new URL(editorUrl, window.location.href);
        url.searchParams.set('section', section);
        url.searchParams.set('slipId', String(slipId));
        return url.toString();
    };

    const prepareContent = () => {
        window.PartialForms?.prepareDynamicContent?.(content);
        content.querySelectorAll('.nomination-row__kind').forEach((kindSelect) => syncCompanionPrice(kindSelect));
        applyPendingPreview();
    };

    const setModalHeading = (section, slip) => {
        if (title) {
            title.textContent = labels[section] || '編集';
        }
        if (table) {
            table.textContent = slip?.tableDisplay || '伝票';
        }
    };

    const renderUnavailable = (message) => {
        state.isSubmitting = false;
        setError(message);
    };

    const openEditor = async (section, slipId) => {
        if (!Object.hasOwn(labels, section) || !slipId) {
            return;
        }

        const requestId = ++state.requestId;
        state.section = section;
        state.slipId = String(slipId);
        state.isSubmitting = false;
        const slip = currentSlip(slipId);
        setModalHeading(section, slip);
        setLoading();
        modal.show();

        try {
            const response = await fetch(buildUrl(section, slipId), {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) {
                throw new Error('Business slip editor load failed.');
            }

            const html = await response.text();
            if (requestId !== state.requestId || !modalElement.classList.contains('show')) {
                return;
            }

            content.innerHTML = html;
            prepareContent();
        } catch {
            if (requestId === state.requestId && modalElement.classList.contains('show')) {
                renderUnavailable('編集内容を取得できませんでした。営業中一覧を再表示してから開き直してください。');
            }
        }
    };

    const syncCompanionPrice = (kindSelect, force = false) => {
        const option = kindSelect?.selectedOptions?.[0];
        if (!option || option.dataset.companion !== 'true') {
            return;
        }

        const priceSelect = kindSelect.closest('form')?.querySelector('.nomination-row__price');
        if (!priceSelect || !Array.from(priceSelect.options).some((item) => item.value === '3000')) {
            return;
        }

        if (force || priceSelect.value === '' || priceSelect.value === '1000') {
            priceSelect.value = '3000';
        }
    };

    const adjustmentHasInput = () => {
        const form = content.querySelector('[data-business-adjustment-form]');
        if (!form) {
            return false;
        }

        const name = form.querySelector('[data-business-adjustment-name]')?.value?.trim() || '';
        const amount = Number(form.querySelector('[data-business-adjustment-amount]')?.value || 0);
        return name.length > 0 || amount !== 0;
    };

    const appendTemporaryRow = (text) => {
        const tableBody = content.querySelector('tbody');
        if (!tableBody) return;
        const row = document.createElement('tr');
        row.className = 'table-info';
        const cell = document.createElement('td');
        cell.colSpan = tableBody.closest('table')?.querySelectorAll('thead th').length || 1;
        cell.textContent = `${text}（保存中）`;
        row.appendChild(cell);
        tableBody.appendChild(row);
    };

    const applyPendingPreview = () => {
        if (!state.slipId || !state.section) return;
        const pending = window.prosperBusinessHomeGetPendingForSlip?.(state.slipId) || [];
        const sectionOperations = pending.filter((operation) => {
            if (state.section === 'customers') return ['add_customer', 'update_customer', 'leave_customer'].includes(operation.operationType);
            if (state.section === 'nominations') return operation.operationType === 'add_nomination';
            return operation.operationType === 'add_adjustment';
        });
        if (sectionOperations.length === 0) return;

        const message = document.createElement('div');
        message.className = 'alert alert-info py-2';
        message.setAttribute('role', 'status');
        message.textContent = `${sectionOperations.length}件の変更を保存中です。`;
        content.prepend(message);

        sectionOperations.forEach((operation) => {
            const payload = operation.payload || {};
            if (operation.operationType === 'add_customer') {
                appendTemporaryRow(`客を追加: ${payload.customer_label?.trim() || '客名なし'} / ${payload.entered_time || '-'}`);
            } else if (operation.operationType === 'add_nomination') {
                appendTemporaryRow(`指名を追加: ${payload.cast_display_name || 'キャスト'} / ${payload.nomination_display_name || payload.nomination_kind || '-'}`);
            } else if (operation.operationType === 'add_adjustment') {
                appendTemporaryRow(`自由入力明細を追加: ${payload.line_name || '-'} / ${payload.amount || 0}円`);
            } else if (operation.operationType === 'update_customer') {
                const input = content.querySelector(`input[name="UpdateCustomerInput.CustomerLabel"]`);
                if (input) input.value = payload.customer_label || '';
            }
        });
    };

    const buildOperation = (form) => {
        const value = (name) => form.querySelector(`[name="${name}"]`)?.value ?? '';
        const slipId = Number(value('SlipId') || state.slipId || 0);
        if (!slipId) return null;

        if (form.querySelector('[name="AddCustomersInput.CustomerLabels[0]"]')) {
            return {
                slipId,
                operationType: 'add_customer',
                payload: {
                    customer_label: value('AddCustomersInput.CustomerLabels[0]'),
                    entered_time: value('AddCustomersInput.EnteredTime')
                }
            };
        }
        if (form.querySelector('[name="UpdateCustomerInput.SlipCustomerId"]')) {
            return {
                slipId,
                operationType: 'update_customer',
                payload: {
                    slip_customer_id: Number(value('UpdateCustomerInput.SlipCustomerId')),
                    customer_label: value('UpdateCustomerInput.CustomerLabel')
                }
            };
        }
        if (form.querySelector('[name="LeaveCustomerInput.SlipCustomerId"]')) {
            return {
                slipId,
                operationType: 'leave_customer',
                payload: {
                    slip_customer_id: Number(value('LeaveCustomerInput.SlipCustomerId')),
                    left_time: value('LeaveCustomerInput.LeftTime')
                }
            };
        }
        if (form.querySelector('[name="AddNominationsInput.CastNominations[0].CastId"]')) {
            const castSelect = form.querySelector('[name="AddNominationsInput.CastNominations[0].CastId"]');
            const kindSelect = form.querySelector('[name="AddNominationsInput.CastNominations[0].NominationKind"]');
            return {
                slipId,
                operationType: 'add_nomination',
                payload: {
                    cast_id: Number(value('AddNominationsInput.CastNominations[0].CastId')),
                    nomination_kind: value('AddNominationsInput.CastNominations[0].NominationKind'),
                    nomination_price: Number(value('AddNominationsInput.CastNominations[0].NominationPrice')),
                    cast_display_name: castSelect?.selectedOptions?.[0]?.textContent?.trim() || '',
                    nomination_display_name: kindSelect?.selectedOptions?.[0]?.textContent?.trim() || ''
                }
            };
        }
        if (form.querySelector('[name="AdjustmentInput.LineName"]')) {
            return {
                slipId,
                operationType: 'add_adjustment',
                payload: {
                    line_name: value('AdjustmentInput.LineName'),
                    amount: Number(value('AdjustmentInput.Amount'))
                }
            };
        }
        return null;
    };

    const submitEditor = (form) => {
        if (state.isSubmitting) {
            return;
        }
        const operation = buildOperation(form);
        if (!operation || !window.prosperBusinessHomeEnqueueEditorOperation) {
            setError('編集内容を確認してください。');
            return;
        }

        state.isSubmitting = true;
        window.prosperBusinessHomeEnqueueEditorOperation(operation);
        modal.hide();
        state.isSubmitting = false;
    };

    document.addEventListener('click', (event) => {
        const button = event.target.closest('[data-business-slip-editor]');
        if (!button) {
            return;
        }

        event.preventDefault();
        void openEditor(button.dataset.businessSlipEditor, button.dataset.businessSlipId);
    });

    document.addEventListener('change', (event) => {
        const kindSelect = event.target.closest('.nomination-row__kind');
        if (kindSelect && content.contains(kindSelect)) {
            syncCompanionPrice(kindSelect, true);
        }
    });

    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-business-slip-editor-form]');
        if (!form || !content.contains(form) || event.defaultPrevented) {
            return;
        }

        event.preventDefault();
        if (!form.checkValidity()) {
            form.reportValidity();
            return;
        }

        void submitEditor(form);
    });

    modalElement.addEventListener('hide.bs.modal', (event) => {
        if (state.isSubmitting || state.section !== 'adjustments' || !adjustmentHasInput()) {
            return;
        }

        if (!window.confirm('自由入力明細を保存せず閉じますか？')) {
            event.preventDefault();
        }
    });

    modalElement.addEventListener('hidden.bs.modal', () => {
        state.requestId += 1;
        state.section = null;
        state.slipId = null;
        state.isSubmitting = false;
        content.innerHTML = '<div class="slip-create__panel slip-detail-panel-loading">編集内容を読み込み中です。</div>';
    });

    document.addEventListener('prosper:business-slips-updated', (event) => {
        if (!state.slipId || state.isSubmitting || !modalElement.classList.contains('show')) {
            return;
        }

        const refreshedSlip = event.detail?.slips?.find((slip) => String(slip.id) === state.slipId);
        if (!refreshedSlip || refreshedSlip.status !== 'open') {
            renderUnavailable('この伝票は会計準備中または会計済みのため編集できません。営業中一覧を再表示してください。');
        }
    });
})();
