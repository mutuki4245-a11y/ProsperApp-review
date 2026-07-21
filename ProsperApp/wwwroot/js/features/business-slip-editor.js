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
        adjustments: '自由明細を編集',
        orders: '注文を編集'
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
    const actionTemplates = new Set(['customer_add', 'customer_rename', 'customer_leave', 'nomination_add', 'adjustment_add', 'order_add']);
    const deleteActions = {
        nomination_delete: { operationType: 'cancel_nomination', recordName: 'SlipCastId', label: 'この指名' },
        adjustment_delete: { operationType: 'void_adjustment', recordName: 'ChargeLineId', label: 'この自由入力明細' },
        order_delete: { operationType: 'void_order', recordName: 'OrderLineId', label: 'この注文' }
    };

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
        content.querySelectorAll('[data-business-order-item]').forEach((itemSelect) => syncOrderBackCast(itemSelect));
        applyPendingPreview();
    };

    const setModalHeading = (section, slip) => {
        if (title) title.textContent = labels[section] || '編集';
        if (table) table.textContent = slip?.tableDisplay || '伝票';
    };

    const renderUnavailable = (message) => {
        state.isSubmitting = false;
        setError(message);
    };

    const openEditor = async (section, slipId) => {
        if (!Object.hasOwn(labels, section) || !slipId) return;

        const requestId = ++state.requestId;
        state.section = section;
        state.slipId = String(slipId);
        state.isSubmitting = false;
        setModalHeading(section, currentSlip(slipId));
        setLoading();
        modal.show();

        try {
            const response = await fetch(buildUrl(section, slipId), {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) throw new Error('Business slip editor load failed.');

            const html = await response.text();
            if (requestId !== state.requestId || !modalElement.classList.contains('show')) return;

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
        if (!option || option.dataset.companion !== 'true') return;

        const priceSelect = kindSelect.closest('form')?.querySelector('.nomination-row__price');
        if (!priceSelect || !Array.from(priceSelect.options).some((item) => item.value === '3000')) return;

        if (force || priceSelect.value === '' || priceSelect.value === '1000') priceSelect.value = '3000';
    };

    const syncOrderBackCast = (itemSelect) => {
        const castSelect = itemSelect?.closest('form')?.querySelector('[data-business-order-back-cast]');
        if (!castSelect) return;
        const canAssignBack = itemSelect.selectedOptions?.[0]?.dataset.businessOrderCastBackTarget === 'true';
        castSelect.disabled = !canAssignBack;
        castSelect.closest('.col-md-3')?.classList.toggle('is-disabled', !canAssignBack);
        if (!canAssignBack) castSelect.value = '';
    };

    const adjustmentHasInput = () => {
        const form = content.querySelector('[data-business-adjustment-form]');
        if (!form) return false;
        const name = form.querySelector('[data-business-adjustment-name]')?.value?.trim() || '';
        const amount = Number(form.querySelector('[data-business-adjustment-amount]')?.value || 0);
        return name.length > 0 || amount !== 0;
    };

    const appendTemporaryRow = (text) => {
        const tableBody = content.querySelector('tbody');
        if (tableBody) {
            const row = document.createElement('tr');
            row.className = 'table-info';
            const cell = document.createElement('td');
            cell.colSpan = tableBody.closest('table')?.querySelectorAll('thead th').length || 1;
            cell.textContent = `${text}（保存中）`;
            row.appendChild(cell);
            tableBody.appendChild(row);
            return;
        }

        const list = content.querySelector('.business-slip-editor-list__rows');
        if (!list) return;
        const row = document.createElement('article');
        row.className = 'business-slip-editor-list__row is-pending';
        const label = document.createElement('span');
        label.textContent = `${text}（保存中）`;
        row.appendChild(label);
        list.appendChild(row);
    };

    const changeActiveCustomerCount = (delta) => {
        const count = content.querySelector('[data-business-customer-active-count]');
        if (!count) return;
        count.textContent = String(Math.max(0, (Number(count.textContent) || 0) + delta));
    };

    const appendTemporaryCustomer = (payload) => {
        const list = content.querySelector('[data-business-customer-active-list]');
        if (!list) return;
        list.querySelector('[data-business-customer-empty]')?.remove();

        const row = document.createElement('article');
        row.className = 'business-customer-editor__row is-pending';
        const customer = document.createElement('div');
        customer.className = 'business-customer-editor__customer';
        customer.append(
            Object.assign(document.createElement('strong'), { textContent: payload.customer_label?.trim() || '客名なし' }),
            Object.assign(document.createElement('span'), { textContent: `入店 ${payload.entered_time || '-'} / 保存中` })
        );
        row.append(customer, Object.assign(document.createElement('span'), { className: 'business-customer-editor__pending', textContent: '追加を保存中' }));
        list.appendChild(row);
        changeActiveCustomerCount(1);
    };

    const findRecordButton = (recordId) => Array.from(content.querySelectorAll('[data-business-editor-record-id]'))
        .find((button) => String(button.dataset.businessEditorRecordId) === String(recordId));

    const markPendingDelete = (recordId, message) => {
        const button = findRecordButton(recordId);
        const row = button?.closest('.business-slip-editor-list__row');
        if (!row) return;
        row.classList.add('is-pending');
        const actions = button.parentElement;
        if (actions) actions.replaceChildren(Object.assign(document.createElement('span'), { className: 'business-customer-editor__pending', textContent: message }));
    };

    const customerRow = (slipCustomerId) => content.querySelector(`[data-business-customer-id="${slipCustomerId}"]`);

    const showPendingCustomerRename = (payload) => {
        const row = customerRow(payload.slip_customer_id);
        if (!row) return;
        row.classList.add('is-pending');
        const label = payload.customer_label?.trim() || '客名なし';
        const name = row.querySelector('.business-customer-editor__customer strong');
        if (name) name.textContent = label;
        const entered = row.querySelector('.business-customer-editor__customer span');
        if (entered && !entered.textContent.includes('保存中')) entered.textContent = `${entered.textContent} / 保存中`;
    };

    const showPendingCustomerLeave = (payload) => {
        const row = customerRow(payload.slip_customer_id);
        if (!row) return;
        row.classList.add('is-pending');
        const actions = row.querySelector('.business-customer-editor__actions');
        if (actions) actions.replaceChildren(Object.assign(document.createElement('span'), { className: 'business-customer-editor__pending', textContent: `退店 ${payload.left_time || '-'} を保存中` }));
        changeActiveCustomerCount(-1);
    };

    const applyPendingPreview = () => {
        if (!state.slipId || !state.section) return;
        const pending = window.prosperBusinessHomeGetPendingForSlip?.(state.slipId) || [];
        const sectionOperations = pending.filter((operation) => {
            if (state.section === 'customers') return ['add_customer', 'update_customer', 'leave_customer'].includes(operation.operationType);
            if (state.section === 'nominations') return ['add_nomination', 'cancel_nomination'].includes(operation.operationType);
            if (state.section === 'adjustments') return ['add_adjustment', 'void_adjustment'].includes(operation.operationType);
            return ['add_order', 'void_order'].includes(operation.operationType);
        });
        if (sectionOperations.length === 0) return;

        const message = document.createElement('div');
        message.className = 'alert alert-info py-2';
        message.setAttribute('role', 'status');
        message.textContent = `${sectionOperations.length}件の変更を保存中です。`;
        content.prepend(message);

        sectionOperations.forEach((operation) => {
            const payload = operation.payload || {};
            if (operation.operationType === 'add_customer') appendTemporaryCustomer(payload);
            else if (operation.operationType === 'update_customer') showPendingCustomerRename(payload);
            else if (operation.operationType === 'leave_customer') showPendingCustomerLeave(payload);
            else if (operation.operationType === 'add_nomination') appendTemporaryRow(`指名を追加: ${payload.cast_display_name || 'キャスト'} / ${payload.nomination_display_name || payload.nomination_kind || '-'}`);
            else if (operation.operationType === 'cancel_nomination') markPendingDelete(payload.slip_cast_id, '削除を保存中');
            else if (operation.operationType === 'add_adjustment') appendTemporaryRow(`自由入力明細を追加: ${payload.line_name || '-'} / ${payload.amount || 0}円`);
            else if (operation.operationType === 'void_adjustment') markPendingDelete(payload.charge_line_id, '削除を保存中');
            else if (operation.operationType === 'add_order') appendTemporaryRow(`注文を追加: ${payload.item_name || '商品'} × ${payload.quantity || 0}`);
            else if (operation.operationType === 'void_order') markPendingDelete(payload.order_line_id, '削除を保存中');
        });
    };

    const showTemplateAction = (action, button) => {
        const manager = content.querySelector('[data-business-slip-editor-manager]');
        const template = manager?.querySelector(`template[data-business-editor-template="${action}"]`);
        if (!manager || !template) return;

        manager.replaceChildren(template.content.cloneNode(true));
        const recordId = button?.dataset.businessEditorRecordId || '';
        const recordLabel = button?.dataset.businessEditorRecordLabel || '';
        const recordDisplay = button?.dataset.businessEditorRecordDisplay || '';
        manager.querySelectorAll('[data-business-editor-record-id-input]').forEach((input) => { input.value = recordId; });
        manager.querySelectorAll('[data-business-editor-record-label-input]').forEach((input) => { input.value = recordLabel || recordDisplay; });
        const heading = manager.querySelector('[data-business-editor-action-heading]');
        if (heading && recordDisplay) heading.textContent = `${recordDisplay}を${action === 'customer_leave' ? '退店' : '変更'}`;
        window.PartialForms?.prepareDynamicContent?.(manager);
        manager.querySelectorAll('.nomination-row__kind').forEach((kindSelect) => syncCompanionPrice(kindSelect));
        manager.querySelectorAll('[data-business-order-item]').forEach((itemSelect) => syncOrderBackCast(itemSelect));
        manager.querySelector('input:not([type="hidden"]), select')?.focus();
    };

    const showDeleteAction = (action, button) => {
        const definition = deleteActions[action];
        const manager = content.querySelector('[data-business-slip-editor-manager]');
        if (!definition || !manager) return;

        const form = document.createElement('form');
        form.className = 'slip-create__panel business-slip-editor-action-form';
        form.dataset.businessSlipEditorForm = '';
        form.dataset.loadingLock = 'false';
        form.dataset.businessOperationType = definition.operationType;
        form.dataset.businessOperationRecordId = button.dataset.businessEditorRecordId || '';
        const slipId = document.createElement('input');
        slipId.type = 'hidden';
        slipId.name = 'SlipId';
        slipId.value = state.slipId || '';
        const titleRow = document.createElement('div');
        titleRow.className = 'business-slip-editor-action-form__title';
        const back = document.createElement('button');
        back.className = 'btn btn-sm btn-outline-secondary';
        back.type = 'button';
        back.dataset.businessEditorAction = 'back';
        back.textContent = '戻る';
        titleRow.append(back, Object.assign(document.createElement('h3'), { textContent: '削除の確認' }));
        const warning = document.createElement('p');
        warning.className = 'mb-3';
        warning.textContent = `${button.dataset.businessEditorRecordDisplay || definition.label} を削除します。`;
        const actions = document.createElement('div');
        actions.className = 'business-slip-editor-action-form__single-row';
        const submit = document.createElement('button');
        submit.type = 'submit';
        submit.className = 'btn btn-danger btn-lg';
        submit.textContent = '削除する';
        actions.appendChild(submit);
        form.append(slipId, titleRow, warning, actions);
        manager.replaceChildren(form);
        submit.focus();
    };

    const buildOperation = (form) => {
        const value = (name) => form.querySelector(`[name="${name}"]`)?.value ?? '';
        const slipId = Number(value('SlipId') || state.slipId || 0);
        if (!slipId) return null;

        if (form.dataset.businessOperationType) {
            const recordId = Number(form.dataset.businessOperationRecordId || 0);
            const operationType = form.dataset.businessOperationType;
            if (!recordId) return null;
            const payload = operationType === 'cancel_nomination'
                ? { slip_cast_id: recordId }
                : operationType === 'void_adjustment'
                    ? { charge_line_id: recordId }
                    : { order_line_id: recordId };
            return { slipId, operationType, payload };
        }

        if (form.querySelector('[name="AddCustomersInput.CustomerLabels[0]"]')) {
            return { slipId, operationType: 'add_customer', payload: { customer_label: value('AddCustomersInput.CustomerLabels[0]'), entered_time: value('AddCustomersInput.EnteredTime') } };
        }
        if (form.querySelector('[name="UpdateCustomerInput.SlipCustomerId"]')) {
            return { slipId, operationType: 'update_customer', payload: { slip_customer_id: Number(value('UpdateCustomerInput.SlipCustomerId')), customer_label: value('UpdateCustomerInput.CustomerLabel') } };
        }
        if (form.querySelector('[name="LeaveCustomerInput.SlipCustomerId"]')) {
            return { slipId, operationType: 'leave_customer', payload: { slip_customer_id: Number(value('LeaveCustomerInput.SlipCustomerId')), left_time: value('LeaveCustomerInput.LeftTime') } };
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
            return { slipId, operationType: 'add_adjustment', payload: { line_name: value('AdjustmentInput.LineName'), amount: Number(value('AdjustmentInput.Amount')) } };
        }
        if (form.querySelector('[name="AddOrderInput.ItemId"]')) {
            const item = form.querySelector('[name="AddOrderInput.ItemId"]')?.selectedOptions?.[0];
            const cast = form.querySelector('[name="AddOrderInput.CastBackCastId"]')?.selectedOptions?.[0];
            return {
                slipId,
                operationType: 'add_order',
                payload: {
                    item_id: Number(value('AddOrderInput.ItemId')),
                    quantity: Number(value('AddOrderInput.Quantity')),
                    cast_back_cast_id: Number(value('AddOrderInput.CastBackCastId')) || null,
                    item_name: item?.dataset.businessOrderName || item?.textContent?.trim() || '',
                    unit_price: Number(item?.dataset.businessOrderPrice) || 0,
                    cast_back_display_name: cast?.dataset.businessOrderCastName || ''
                }
            };
        }
        return null;
    };

    const submitEditor = (form) => {
        if (state.isSubmitting) return;
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
        const actionButton = event.target.closest('[data-business-editor-action]');
        if (actionButton && content.contains(actionButton)) {
            event.preventDefault();
            const action = actionButton.dataset.businessEditorAction;
            if (action === 'back') {
                void openEditor(state.section, state.slipId);
            } else if (actionTemplates.has(action)) {
                showTemplateAction(action, actionButton);
            } else if (Object.hasOwn(deleteActions, action)) {
                showDeleteAction(action, actionButton);
            }
            return;
        }

        const button = event.target.closest('[data-business-slip-editor]');
        if (!button) return;
        event.preventDefault();
        void openEditor(button.dataset.businessSlipEditor, button.dataset.businessSlipId);
    });

    document.addEventListener('change', (event) => {
        const kindSelect = event.target.closest('.nomination-row__kind');
        if (kindSelect && content.contains(kindSelect)) syncCompanionPrice(kindSelect, true);
        const itemSelect = event.target.closest('[data-business-order-item]');
        if (itemSelect && content.contains(itemSelect)) syncOrderBackCast(itemSelect);
    });

    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-business-slip-editor-form]');
        if (!form || !content.contains(form) || event.defaultPrevented) return;
        event.preventDefault();
        if (!form.checkValidity()) {
            form.reportValidity();
            return;
        }
        submitEditor(form);
    });

    modalElement.addEventListener('hide.bs.modal', (event) => {
        if (state.isSubmitting || state.section !== 'adjustments' || !adjustmentHasInput()) return;
        if (!window.confirm('自由入力明細を保存せず閉じますか？')) event.preventDefault();
    });

    modalElement.addEventListener('hidden.bs.modal', () => {
        state.requestId += 1;
        state.section = null;
        state.slipId = null;
        state.isSubmitting = false;
        content.innerHTML = '<div class="slip-create__panel slip-detail-panel-loading">編集内容を読み込み中です。</div>';
    });

    document.addEventListener('prosper:business-slips-updated', (event) => {
        if (!state.slipId || state.isSubmitting || !modalElement.classList.contains('show')) return;
        const refreshedSlip = event.detail?.slips?.find((slip) => String(slip.id) === state.slipId);
        if (!refreshedSlip || refreshedSlip.status !== 'open') {
            renderUnavailable('この伝票は会計準備中または会計済みのため編集できません。営業中一覧を再表示してください。');
        }
    });
})();
