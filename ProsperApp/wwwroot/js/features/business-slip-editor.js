(() => {
    const config = window.prosperBusinessHome ?? {};
    const editorUrl = config.businessSlipEditorUrl || '';
    const modalElement = document.querySelector('[data-business-slip-editor-modal]');
    const content = modalElement?.querySelector('[data-business-slip-editor-content]');
    const title = modalElement?.querySelector('[data-business-slip-editor-title]');
    const backTargetModalElement = document.querySelector('[data-business-order-back-target-modal]');
    const backTargetList = backTargetModalElement?.querySelector('[data-business-order-back-target-list]');
    const backTargetConfirm = backTargetModalElement?.querySelector('[data-business-order-back-target-confirm]');
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
        isSubmitting: false,
        showingAction: false,
        orderQueue: new Map(),
        pendingBackItemId: null,
        pendingBackSelection: new Map()
    };

    if (!modalElement || !content || !window.bootstrap?.Modal) {
        return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const backTargetModal = backTargetModalElement ? bootstrap.Modal.getOrCreateInstance(backTargetModalElement) : null;
    const currentSlip = (slipId) => (window.prosperBusinessHomeSlips || []).find((slip) => String(slip.id) === String(slipId));
    const formatYen = window.MoneyText?.yen ?? ((amount) => `${Number(amount || 0).toLocaleString('ja-JP')}円`);
    const actionTemplates = new Set(['customer_add', 'customer_rename', 'customer_leave', 'nomination_add', 'adjustment_add', 'order_add']);
    const deleteActions = {
        nomination_delete: { operationType: 'cancel_nomination', recordName: 'SlipCastId', label: 'この指名' },
        adjustment_delete: { operationType: 'void_adjustment', recordName: 'ChargeLineId', label: 'この自由入力明細' },
        order_delete: { operationType: 'void_order', recordName: 'OrderLineId', label: 'この注文' }
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
        if (title) title.textContent = labels[section] || '編集';
    };

    const renderUnavailable = (message) => {
        state.isSubmitting = false;
        setError(message);
    };

    const appendEditorAction = (row, action, text, record = {}) => {
        const button = document.createElement('button');
        button.className = action.endsWith('_delete') ? 'btn btn-sm btn-outline-danger' : 'btn btn-sm btn-outline-primary';
        button.type = 'button';
        button.dataset.businessEditorAction = action;
        if (record.id !== undefined) button.dataset.businessEditorRecordId = String(record.id);
        if (record.label !== undefined) button.dataset.businessEditorRecordLabel = record.label || '';
        if (record.display !== undefined) button.dataset.businessEditorRecordDisplay = record.display || '';
        button.textContent = text;
        row.appendChild(button);
    };

    const createLocalEditorRow = (primary, secondary) => {
        const row = document.createElement('article');
        row.className = 'business-slip-editor-list__row';
        const text = document.createElement('div');
        text.appendChild(Object.assign(document.createElement('strong'), { textContent: primary }));
        if (secondary) text.appendChild(Object.assign(document.createElement('span'), { textContent: secondary }));
        row.appendChild(text);
        return row;
    };

    const renderLocalEditor = (section, slip) => {
        if (!slip || slip.status !== 'open') {
            renderUnavailable('この伝票は会計準備中または会計済みのため編集できません。営業中一覧を再表示してください。');
            return;
        }

        const manager = document.createElement('div');
        manager.dataset.businessSlipEditorManager = '';
        const panel = document.createElement('section');
        panel.className = 'slip-create__panel business-slip-editor-list';
        const titleRow = document.createElement('div');
        titleRow.className = 'slip-create__panel-title';
        const actionLabel = {
            customers: '客を追加',
            nominations: '指名を追加',
            adjustments: '明細を追加',
            orders: '注文を追加'
        }[section] || '追加';
        const add = document.createElement('button');
        add.className = 'btn btn-sm btn-primary';
        add.type = 'button';
        add.dataset.businessEditorAction = `${section === 'customers' ? 'customer' : section.slice(0, -1)}_add`;
        add.textContent = actionLabel;
        titleRow.appendChild(add);
        const rows = document.createElement('div');
        rows.className = 'business-slip-editor-list__rows';

        if (section === 'customers') {
            (slip.customers || []).filter((item) => item.status === 'active').forEach((customer) => {
                const row = createLocalEditorRow(customer.displayName || '客名なし', `入店 ${customer.enteredTime || '-'}`);
                row.dataset.businessCustomerRow = '';
                row.dataset.businessCustomerId = String(customer.id);
                appendEditorAction(row, 'customer_rename', '名前変更', { id: customer.id, label: customer.customerLabel, display: customer.displayName });
                appendEditorAction(row, 'customer_leave', '退店', { id: customer.id, display: customer.displayName });
                rows.appendChild(row);
            });
        } else if (section === 'nominations') {
            (slip.nominations || []).filter((item) => item.status === 'active').forEach((nomination) => {
                const row = createLocalEditorRow(nomination.displayName || 'キャスト', nomination.nominationDisplayName || nomination.nominationKind || '指名');
                appendEditorAction(row, 'nomination_delete', '削除', { id: nomination.id, display: `${nomination.displayName || 'キャスト'}（${nomination.nominationDisplayName || nomination.nominationKind || '指名'}）` });
                rows.appendChild(row);
            });
        } else if (section === 'adjustments') {
            (slip.adjustments || []).filter((item) => item.status === 'active').forEach((adjustment) => {
                const row = createLocalEditorRow(adjustment.lineName || '-', formatYen(adjustment.amount));
                appendEditorAction(row, 'adjustment_delete', '削除', { id: adjustment.id, display: adjustment.lineName || '自由入力明細' });
                rows.appendChild(row);
            });
        } else {
            (slip.orders || []).filter((item) => item.status === 'active' && item.itemType === 'standard').forEach((order) => {
                const back = order.backCastDisplayName ? ` / ${order.backCastDisplayName}` : '';
                const row = createLocalEditorRow(order.itemName || '商品', `${formatYen(order.unitPrice)} × ${order.quantity || 0}${back} / ${formatYen(order.amount)}`);
                appendEditorAction(row, 'order_delete', '削除', { id: order.id, display: order.itemName || '注文' });
                rows.appendChild(row);
            });
        }

        if (!rows.childElementCount) {
            rows.appendChild(Object.assign(document.createElement('p'), { className: 'text-muted mb-0', textContent: '登録はありません。' }));
        }
        panel.append(titleRow, rows);
        manager.appendChild(panel);
        content.replaceChildren(manager);
    };

    const loadEditorMarkup = async (section, slipId, force = false) => {
        if (!editorUrl) return false;
        const requestId = state.requestId;
        try {
            const response = await fetch(buildUrl(section, slipId), { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok) throw new Error('Business slip editor load failed.');
            const html = await response.text();
            if (requestId !== state.requestId || (!force && state.showingAction) || !modalElement.classList.contains('show')) return false;
            content.innerHTML = html;
            prepareContent();
            return true;
        } catch {
            return false;
        }
    };

    const openEditor = (section, slipId) => {
        if (!Object.hasOwn(labels, section) || !slipId) return;
        state.requestId += 1;
        state.section = section;
        state.slipId = String(slipId);
        state.isSubmitting = false;
        state.showingAction = false;
        setModalHeading(section, currentSlip(slipId));
        renderLocalEditor(section, currentSlip(slipId));
        modal.show();
    };

    const syncCompanionPrice = (kindSelect, force = false) => {
        const option = kindSelect?.selectedOptions?.[0];
        if (!option || option.dataset.companion !== 'true') return;

        const priceSelect = kindSelect.closest('form')?.querySelector('.nomination-row__price');
        if (!priceSelect || !Array.from(priceSelect.options).some((item) => item.value === '3000')) return;

        if (force || priceSelect.value === '' || priceSelect.value === '1000') priceSelect.value = '3000';
    };

    const businessOrderQueueKey = (itemId, castBackCastId) => `${itemId}:${castBackCastId ?? ''}`;

    const businessOrderQueueForm = () => content.querySelector('[data-business-order-queue-form]');

    const businessOrderItem = (form, itemId) => form?.querySelector(`[data-slip-order-catalog-item="${itemId}"]`);

    const businessOrderCastOptions = (form) => Array.from(form?.querySelectorAll('[data-business-order-cast-id]') ?? [])
        .map((cast) => ({
            id: cast.dataset.businessOrderCastId || '',
            name: cast.dataset.businessOrderCastName || '',
            drinkMemo: cast.dataset.businessOrderCastMemo || ''
        }))
        .filter((cast) => cast.id);

    const hideBusinessOrderBackPicker = () => {
        state.pendingBackItemId = null;
        state.pendingBackSelection.clear();
        if (backTargetModalElement?.classList.contains('show')) backTargetModal?.hide();
    };

    const renderBusinessOrderQueue = (form = businessOrderQueueForm()) => {
        if (!form) return;
        const fields = form.querySelector('#detailOrderQueueFields');
        const list = form.querySelector('#detailOrderQueueList');
        const serialized = form.querySelector('#detailOrderQueueJson');
        const empty = form.querySelector('#detailOrderQueueEmpty');
        const status = form.querySelector('#detailOrderQueueStatus');
        const total = form.querySelector('#detailOrderQueueTotal');
        const submit = form.querySelector('#detailSubmitOrderButton');
        if (!fields || !list || !serialized || !empty || !status || !total || !submit) return;

        fields.replaceChildren();
        list.replaceChildren();
        const lines = [];
        let queueTotal = 0;

        state.orderQueue.forEach((line, key) => {
            const item = businessOrderItem(form, line.itemId);
            if (!item || line.quantity <= 0) return;
            const price = Number(item.dataset.slipOrderCatalogPrice) || 0;
            const subtotal = price * line.quantity;
            const cast = line.castBackCastId
                ? businessOrderCastOptions(form).find((candidate) => String(candidate.id) === String(line.castBackCastId))
                : null;
            const index = lines.length;
            lines.push({ itemId: Number(line.itemId), quantity: line.quantity, castBackCastId: line.castBackCastId ? Number(line.castBackCastId) : null });
            queueTotal += subtotal;

            fields.insertAdjacentHTML('beforeend', `
                <input type="hidden" name="QueueLines[${index}].ItemId" value="${line.itemId}" />
                <input type="hidden" name="QueueLines[${index}].Quantity" value="${line.quantity}" />
                ${line.castBackCastId ? `<input type="hidden" name="QueueLines[${index}].CastBackCastId" value="${line.castBackCastId}" />` : ''}
            `);

            const row = document.createElement('div');
            row.className = 'order-queue__row';
            const main = document.createElement('div');
            main.className = 'order-queue__row-main';
            main.appendChild(Object.assign(document.createElement('strong'), { textContent: item.dataset.slipOrderCatalogName || '商品' }));
            if (cast) {
                main.appendChild(Object.assign(document.createElement('small'), {
                    className: 'order-queue__back',
                    textContent: cast.name
                }));
            }
            const amount = document.createElement('div');
            amount.className = 'order-queue__row-amount';
            amount.append(
                Object.assign(document.createElement('span'), { textContent: `${formatYen(price)} x ${line.quantity}` }),
                Object.assign(document.createElement('strong'), { textContent: formatYen(subtotal) })
            );
            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'btn btn-outline-danger btn-sm';
            remove.dataset.businessOrderQueueRemove = key;
            remove.textContent = '削除';
            row.append(main, amount, remove);
            list.appendChild(row);
        });

        serialized.value = JSON.stringify(lines);
        const hasQueue = lines.length > 0;
        empty.hidden = hasQueue;
        status.textContent = hasQueue ? '未送信' : '空';
        status.dataset.saveState = hasQueue ? 'dirty' : 'saved';
        total.textContent = formatYen(queueTotal);
        submit.disabled = submit.dataset.submitBaseDisabled === 'true' || !hasQueue;
    };

    const addBusinessOrderToQueue = (form, itemId, castBackCastId = null) => {
        const key = businessOrderQueueKey(itemId, castBackCastId);
        const line = state.orderQueue.get(key) ?? { itemId: String(itemId), castBackCastId: castBackCastId ? String(castBackCastId) : null, quantity: 0 };
        line.quantity += 1;
        state.orderQueue.set(key, line);
        renderBusinessOrderQueue(form);
    };

    const changeBusinessOrderBackSelection = (castId, delta) => {
        const key = String(castId ?? '');
        const current = state.pendingBackSelection.get(key) ?? 0;
        const next = Math.max(0, current + delta);
        if (key === '' && next > 0) {
            state.pendingBackSelection.clear();
        } else if (key !== '') {
            state.pendingBackSelection.delete('');
        }
        if (next > 0) state.pendingBackSelection.set(key, next);
        else state.pendingBackSelection.delete(key);
        renderBusinessOrderBackPicker();
    };

    const renderBusinessOrderBackPicker = () => {
        const form = businessOrderQueueForm();
        if (!form || !backTargetList) return;
        const nominatedCastIds = new Set((currentSlip(state.slipId)?.nominations || [])
            .filter((nomination) => nomination.status === 'active')
            .map((nomination) => String(nomination.castId)));
        const casts = [{ id: '', name: '指定なし', drinkMemo: '' }, ...businessOrderCastOptions(form)];
        backTargetList.replaceChildren();
        casts.forEach((cast) => {
            const key = String(cast.id);
            const count = state.pendingBackSelection.get(key) ?? 0;
            const row = document.createElement('div');
            row.className = 'cast-select-modal__item cast-select-modal__item--stepper';
            row.classList.toggle('is-selected', count > 0);
            row.classList.toggle('is-nominated', key !== '' && nominatedCastIds.has(key));

            const label = document.createElement('div');
            label.className = 'cast-select-modal__item-label';
            const displayName = cast.drinkMemo ? `${cast.name}（${cast.drinkMemo}）` : cast.name;
            label.appendChild(Object.assign(document.createElement('strong'), { textContent: displayName }));
            if (key !== '' && nominatedCastIds.has(key)) {
                label.appendChild(Object.assign(document.createElement('span'), { textContent: '指名' }));
            }

            const controls = document.createElement('div');
            controls.className = 'cast-select-modal__stepper';
            const decrement = document.createElement('button');
            decrement.type = 'button';
            decrement.className = 'btn btn-outline-secondary btn-sm';
            decrement.textContent = '-';
            decrement.disabled = count <= 0;
            decrement.addEventListener('click', () => changeBusinessOrderBackSelection(cast.id, -1));
            const quantity = document.createElement('strong');
            quantity.textContent = String(count);
            quantity.setAttribute('aria-label', `${displayName} ${count}件`);
            const increment = document.createElement('button');
            increment.type = 'button';
            increment.className = 'btn btn-outline-primary btn-sm';
            increment.textContent = '+';
            increment.addEventListener('click', () => changeBusinessOrderBackSelection(cast.id, 1));
            controls.append(decrement, quantity, increment);
            row.append(label, controls);
            backTargetList.appendChild(row);
        });

        if (backTargetConfirm) {
            const total = Array.from(state.pendingBackSelection.values()).reduce((sum, count) => sum + count, 0);
            backTargetConfirm.disabled = !state.pendingBackItemId || total <= 0;
        }
    };

    const showBusinessOrderBackPicker = (form, itemId) => {
        if (!form || !backTargetModal || !backTargetList) return;
        state.pendingBackItemId = String(itemId);
        state.pendingBackSelection.clear();
        renderBusinessOrderBackPicker();
        backTargetModal.show();
    };

    const selectBusinessOrderCategory = (tab) => {
        const form = tab.closest('[data-business-order-queue-form]');
        if (!form) return;
        const index = tab.dataset.slipOrderCatalogTab || '';
        form.querySelectorAll('[data-slip-order-catalog-tab]').forEach((candidate) => {
            candidate.classList.toggle('is-active', candidate === tab);
        });
        form.querySelectorAll('[data-slip-order-catalog-panel]').forEach((panel) => {
            panel.classList.toggle('is-active', panel.dataset.slipOrderCatalogPanel === index);
        });
    };

    backTargetConfirm?.addEventListener('click', () => {
        const form = businessOrderQueueForm();
        if (!form || !state.pendingBackItemId || state.pendingBackSelection.size === 0) return;
        state.pendingBackSelection.forEach((count, castId) => {
            for (let index = 0; index < count; index += 1) {
                addBusinessOrderToQueue(form, state.pendingBackItemId, castId || null);
            }
        });
        hideBusinessOrderBackPicker();
    });

    backTargetModalElement?.addEventListener('hidden.bs.modal', () => {
        state.pendingBackItemId = null;
        state.pendingBackSelection.clear();
        if (backTargetConfirm) backTargetConfirm.disabled = true;
    });

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
        if (!manager || !template) {
            if (!state.section || !state.slipId) return;
            state.showingAction = true;
            content.innerHTML = '<div class="slip-create__panel slip-detail-panel-loading">入力内容を準備しています。</div>';
            void loadEditorMarkup(state.section, state.slipId, true).then((loaded) => {
                if (loaded) showTemplateAction(action, button);
                else renderUnavailable('入力内容を取得できませんでした。営業中一覧を再表示してから開き直してください。');
            });
            return;
        }

        state.showingAction = true;
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
        if (action === 'order_add') {
            state.orderQueue.clear();
            state.pendingBackItemId = null;
            renderBusinessOrderQueue(manager.querySelector('[data-business-order-queue-form]'));
        }
        manager.querySelector('input:not([type="hidden"]), select')?.focus();
    };

    const showDeleteAction = (action, button) => {
        const definition = deleteActions[action];
        const manager = content.querySelector('[data-business-slip-editor-manager]');
        if (!definition || !manager) return;

        state.showingAction = true;

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
        if (form.hasAttribute('data-business-order-queue-form')) {
            let queueLines;
            try {
                queueLines = JSON.parse(value('OrderQueueJson') || '[]');
            } catch {
                return [];
            }
            if (!Array.isArray(queueLines)) return [];
            return queueLines
                .filter((line) => Number(line?.itemId) > 0 && Number(line?.quantity) > 0)
                .map((line) => {
                    const item = businessOrderItem(form, line.itemId);
                    const cast = line.castBackCastId
                        ? businessOrderCastOptions(form).find((candidate) => String(candidate.id) === String(line.castBackCastId))
                        : null;
                    return {
                        slipId,
                        operationType: 'add_order',
                        payload: {
                            item_id: Number(line.itemId),
                            quantity: Number(line.quantity),
                            cast_back_cast_id: line.castBackCastId ? Number(line.castBackCastId) : null,
                            item_name: item?.dataset.slipOrderCatalogName || '',
                            unit_price: Number(item?.dataset.slipOrderCatalogPrice) || 0,
                            cast_back_display_name: cast?.name || ''
                        }
                    };
                });
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
        const operations = (Array.isArray(operation) ? operation : [operation]).filter(Boolean);
        if (operations.length === 0 || !window.prosperBusinessHomeEnqueueEditorOperation) {
            const queueError = form.querySelector('[data-business-order-queue-error]');
            if (queueError) {
                queueError.textContent = '商品を選択してください。';
            } else {
                setError('編集内容を確認してください。');
            }
            return;
        }
        state.isSubmitting = true;
        operations.forEach((nextOperation) => window.prosperBusinessHomeEnqueueEditorOperation(nextOperation));
        modal.hide();
        state.isSubmitting = false;
    };

    document.addEventListener('click', (event) => {
        const orderItemButton = event.target.closest('[data-slip-order-catalog-item]');
        if (orderItemButton && content.contains(orderItemButton)) {
            const form = orderItemButton.closest('[data-business-order-queue-form]');
            if (form) {
                event.preventDefault();
                if (orderItemButton.dataset.slipOrderCatalogCastBackTarget === 'true') {
                    showBusinessOrderBackPicker(form, orderItemButton.dataset.slipOrderCatalogItem || '');
                } else {
                    addBusinessOrderToQueue(form, orderItemButton.dataset.slipOrderCatalogItem || '');
                }
                return;
            }
        }

        const orderCategoryTab = event.target.closest('[data-slip-order-catalog-tab]');
        if (orderCategoryTab && content.contains(orderCategoryTab) && orderCategoryTab.closest('[data-business-order-queue-form]')) {
            event.preventDefault();
            selectBusinessOrderCategory(orderCategoryTab);
            return;
        }

        const orderQueueRemove = event.target.closest('[data-business-order-queue-remove]');
        if (orderQueueRemove && content.contains(orderQueueRemove)) {
            const form = orderQueueRemove.closest('[data-business-order-queue-form]');
            if (form) {
                event.preventDefault();
                state.orderQueue.delete(orderQueueRemove.dataset.businessOrderQueueRemove || '');
                renderBusinessOrderQueue(form);
                return;
            }
        }

        const clearOrderQueue = event.target.closest('#detailClearQueueButton');
        if (clearOrderQueue && content.contains(clearOrderQueue)) {
            const form = clearOrderQueue.closest('[data-business-order-queue-form]');
            if (form) {
                event.preventDefault();
                state.orderQueue.clear();
                hideBusinessOrderBackPicker(form);
                renderBusinessOrderQueue(form);
                return;
            }
        }

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
        state.showingAction = false;
        state.orderQueue.clear();
        state.pendingBackItemId = null;
        state.pendingBackSelection.clear();
        content.replaceChildren();
    });

    document.addEventListener('prosper:business-slips-updated', (event) => {
        if (!state.slipId || state.isSubmitting || !modalElement.classList.contains('show')) return;
        const refreshedSlip = event.detail?.slips?.find((slip) => String(slip.id) === state.slipId);
        if (!refreshedSlip || refreshedSlip.status !== 'open') {
            renderUnavailable('この伝票は会計準備中または会計済みのため編集できません。営業中一覧を再表示してください。');
        }
    });
})();
