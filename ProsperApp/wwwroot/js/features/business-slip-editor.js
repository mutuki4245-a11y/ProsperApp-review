(() => {
    const config = window.prosperBusinessHome ?? {};
    const editorOptions = config.editorOptions ?? {};
    const modalElement = document.querySelector('[data-business-slip-editor-modal]');
    const content = modalElement?.querySelector('[data-business-slip-editor-content]');
    const title = modalElement?.querySelector('[data-business-slip-editor-title]');
    const modalFooter = modalElement?.querySelector('[data-business-slip-editor-footer]');
    const backTargetModalElement = document.querySelector('[data-business-order-back-target-modal]');
    const backTargetList = backTargetModalElement?.querySelector('[data-business-order-back-target-list]');
    const backTargetConfirm = backTargetModalElement?.querySelector('[data-business-order-back-target-confirm]');
    const attendingCastModalElement = document.getElementById('businessAttendingCastSelectModal');
    const attendingCastList = document.getElementById('businessAttendingCastModalList');
    const labels = {
        customers: '客を編集',
        nominations: '指名を編集',
        adjustments: '自由明細を編集',
        orders: '注文を編集'
    };
    const state = {
        section: null,
        slipId: null,
        isSubmitting: false,
        orderQueue: new Map(),
        pendingBackItemId: null,
        pendingBackSelection: new Map()
    };

    if (!modalElement || !content || !window.bootstrap?.Modal) {
        return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const backTargetModal = backTargetModalElement ? bootstrap.Modal.getOrCreateInstance(backTargetModalElement) : null;
    const attendingCastModal = attendingCastModalElement ? bootstrap.Modal.getOrCreateInstance(attendingCastModalElement) : null;
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

    const setModalHeading = (section, slip) => {
        if (title) title.textContent = labels[section] || '編集';
    };

    const renderUnavailable = (message) => {
        state.isSubmitting = false;
        setError(message);
    };

    const resetFooterAction = () => {
        modalFooter?.querySelector('[data-business-slip-editor-submit]')?.remove();
    };

    const setFooterAction = (form, submit) => {
        if (!modalFooter || !form || !submit) return;
        resetFooterAction();
        if (!form.id) form.id = 'businessSlipEditorActionForm';
        submit.dataset.businessSlipEditorSubmit = '';
        submit.setAttribute('form', form.id);
        modalFooter.appendChild(submit);
    };

    const appendEditorAction = (row, action, text, record = {}) => {
        const button = document.createElement('button');
        button.className = action === 'customer_leave'
            ? 'btn btn-sm btn-danger'
            : action.endsWith('_delete') ? 'btn btn-sm btn-outline-danger' : 'btn btn-sm btn-outline-primary';
        button.type = 'button';
        button.dataset.businessEditorAction = action;
        if (record.id !== undefined) button.dataset.businessEditorRecordId = String(record.id);
        if (record.label !== undefined) button.dataset.businessEditorRecordLabel = record.label || '';
        if (record.display !== undefined) button.dataset.businessEditorRecordDisplay = record.display || '';
        button.textContent = text;
        let actions = row.querySelector('[data-business-editor-row-actions]');
        if (!actions) {
            actions = document.createElement('div');
            actions.className = 'business-slip-editor-list__actions';
            actions.dataset.businessEditorRowActions = '';
            row.appendChild(actions);
        }
        actions.appendChild(button);
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
        resetFooterAction();
        modalElement.classList.remove('is-order-editor');
        if (!slip || slip.status !== 'open') {
            renderUnavailable('この伝票は会計準備中または会計済みのため編集できません。営業中一覧を再表示してください。');
            return;
        }

        const manager = document.createElement('div');
        manager.dataset.businessSlipEditorManager = '';
        const panel = document.createElement('section');
        panel.className = 'slip-create__panel business-slip-editor-list';
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
        const addUnavailable =
            (section === 'nominations' && (configuredCasts().length === 0 || configuredNominationOptions().length === 0)) ||
            (section === 'orders' && configuredOrderItems().length === 0);
        add.disabled = addUnavailable;
        if (addUnavailable) {
            add.title = section === 'nominations' ? '出勤キャストと指名区分を登録してください。' : '注文可能な商品を登録してください。';
        }
        const rows = document.createElement('div');
        rows.className = 'business-slip-editor-list__rows';

        if (section === 'customers') {
            (slip.customers || []).filter((item) => item.status === 'active').forEach((customer) => {
                const enteredTime = customer.enteredTime || '-';
                const row = createLocalEditorRow(`${customer.displayName || '客名なし'}（${enteredTime}）`);
                row.classList.add('business-slip-editor-list__row--customer');
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
            (slip.orders || [])
                .filter((item) => item.status === 'active' && item.itemType === 'standard')
                .sort((left, right) => String(left.orderedAt || left.orderedTime || '').localeCompare(String(right.orderedAt || right.orderedTime || '')) || (Number(left.lineNo) || 0) - (Number(right.lineNo) || 0))
                .forEach((order) => {
                const back = order.backCastDisplayName ? ` / ${order.backCastDisplayName}` : '';
                const orderTime = order.orderedTime || (() => {
                    const orderedAt = new Date(order.orderedAt || '');
                    return Number.isNaN(orderedAt.getTime()) ? '-' : `${String(orderedAt.getHours()).padStart(2, '0')}:${String(orderedAt.getMinutes()).padStart(2, '0')}`;
                })();
                const row = createLocalEditorRow(`${orderTime} ${order.itemName || '商品'}${back}`, `× ${order.quantity || 0}　${formatYen(order.amount)}`);
                row.classList.add('business-slip-editor-list__row--order');
                appendEditorAction(row, 'order_delete', '削除', { id: order.id, display: order.itemName || '注文' });
                rows.appendChild(row);
            });
        }

        if (!rows.childElementCount) {
            rows.appendChild(Object.assign(document.createElement('p'), { className: 'text-muted mb-0', textContent: '登録はありません。' }));
        }
        const footer = document.createElement('div');
        footer.className = 'business-slip-editor-list__footer';
        footer.appendChild(add);
        panel.append(rows, footer);
        manager.appendChild(panel);
        content.replaceChildren(manager);
    };

    const openEditor = (section, slipId) => {
        if (!Object.hasOwn(labels, section) || !slipId) return;
        state.section = section;
        state.slipId = String(slipId);
        state.isSubmitting = false;
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

    const configuredTimeOptions = () => Array.isArray(editorOptions.timeOptions) ? editorOptions.timeOptions : [];
    const configuredCasts = () => Array.isArray(editorOptions.attendanceCasts) ? editorOptions.attendanceCasts : [];
    const configuredNominationOptions = () => Array.isArray(editorOptions.nominationOptions) ? editorOptions.nominationOptions : [];
    const configuredNominationPrices = () => Array.isArray(editorOptions.nominationPriceOptions) ? editorOptions.nominationPriceOptions : [];
    const configuredOrderItems = () => Array.isArray(editorOptions.orderItems) ? editorOptions.orderItems : [];
    const activeNominationCastIds = () => new Set(
        (currentSlip(state.slipId)?.nominations || [])
            .filter((nomination) => nomination.status === 'active')
            .map((nomination) => String(nomination.castId || ''))
            .filter(Boolean)
    );
    const availableNominationCasts = (allowDuplicates = false) => {
        if (allowDuplicates) return configuredCasts();
        const selectedCastIds = activeNominationCastIds();
        return configuredCasts().filter((cast) => !selectedCastIds.has(String(cast.id)));
    };
    const allowsDuplicateNominations = (form) => form?.querySelector('[data-business-editor-allow-nomination-duplicates]')?.checked === true;

    const currentStoreTime = () => {
        const now = new Date();
        const minute = Math.floor(now.getMinutes() / 5) * 5;
        const value = `${String(now.getHours()).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
        return configuredTimeOptions().some((option) => option.value === value)
            ? value
            : configuredTimeOptions()[0]?.value || '';
    };

    const createActionForm = (heading, submitLabel, formClass = '') => {
        const form = document.createElement('form');
        form.className = `slip-create__panel business-slip-editor-action-form ${formClass}`.trim();
        form.id = 'businessSlipEditorActionForm';
        form.dataset.businessSlipEditorForm = '';
        form.dataset.loadingLock = 'false';

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
        titleRow.append(back, Object.assign(document.createElement('h3'), { textContent: heading }));
        const fields = document.createElement('div');
        fields.className = 'business-slip-editor-action-fields';
        const submit = document.createElement('button');
        submit.className = 'btn btn-primary btn-lg';
        submit.type = 'submit';
        submit.textContent = submitLabel;
        form.append(slipId, titleRow, fields, submit);
        return { form, fields, submit };
    };

    const appendField = (fields, labelText, control, className = '') => {
        const field = document.createElement('div');
        field.className = `business-slip-editor-action-field ${className}`.trim();
        const label = document.createElement('label');
        label.className = 'form-label';
        label.textContent = labelText;
        field.append(label, control);
        fields.appendChild(field);
        return field;
    };

    const appendSelectOptions = (select, options, selectedValue, placeholder = '') => {
        if (placeholder) {
            select.appendChild(Object.assign(document.createElement('option'), { value: '', textContent: placeholder }));
        }
        options.forEach((option) => {
            const element = document.createElement('option');
            element.value = String(option.value ?? option.id ?? '');
            element.textContent = option.label ?? option.name ?? '';
            element.selected = String(element.value) === String(selectedValue ?? '');
            if (option.isCompanion) element.dataset.companion = 'true';
            select.appendChild(element);
        });
    };

    const createTimeSelect = (name, selectedValue = currentStoreTime()) => {
        const select = document.createElement('select');
        select.className = 'form-select form-select-lg slip-time-select';
        select.name = name;
        appendSelectOptions(select, configuredTimeOptions(), selectedValue);
        return select;
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
        const submit = form.querySelector('#detailSubmitOrderButton') || modalFooter?.querySelector('#detailSubmitOrderButton');
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
            main.appendChild(Object.assign(document.createElement('strong'), {
                textContent: `${item.dataset.slipOrderCatalogName || '商品'}${cast ? ` / ${cast.name}` : ''}`
            }));
            const amount = document.createElement('div');
            amount.className = 'order-queue__row-amount';
            amount.textContent = `× ${line.quantity}　${formatYen(subtotal)}`;
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
        modalElement.classList.add('is-child-modal-active');
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
        modalElement.classList.remove('is-child-modal-active');
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

    const openNominationCastPicker = (form) => {
        const castId = form?.querySelector('[name="AddNominationsInput.CastNominations[0].CastId"]');
        const castName = form?.querySelector('[data-business-editor-cast-display]');
        if (!castId || !castName || !attendingCastModal || !attendingCastList || !window.CastSelectModal) return;
        window.CastSelectModal.renderRequired(attendingCastList, availableNominationCasts(allowsDuplicateNominations(form)), {
            getLabel: (cast) => cast.name || '',
            emptyMessage: '追加できる出勤キャストがいません。',
            onSelect: (cast) => {
                castId.value = String(cast.id || '');
                castName.textContent = cast.name || 'キャストを選択';
                attendingCastModal.hide();
            }
        });
        modalElement.classList.add('is-child-modal-active');
        attendingCastModal.show();
    };

    const createOrderAddForm = () => {
        const { form, fields, submit } = createActionForm('注文を追加', '注文を追加', 'business-slip-editor-order-form');
        form.dataset.businessOrderQueueForm = '';
        const queueJson = document.createElement('input');
        queueJson.type = 'hidden';
        queueJson.name = 'OrderQueueJson';
        queueJson.id = 'detailOrderQueueJson';
        queueJson.value = '[]';
        const error = document.createElement('span');
        error.className = 'text-danger';
        error.dataset.businessOrderQueueError = '';
        const casts = document.createElement('div');
        casts.hidden = true;
        casts.dataset.businessOrderCasts = '';
        configuredCasts().forEach((cast) => {
            const item = document.createElement('span');
            item.dataset.businessOrderCastId = String(cast.id);
            item.dataset.businessOrderCastName = cast.name || '';
            item.dataset.businessOrderCastMemo = cast.drinkMemo || '';
            casts.appendChild(item);
        });
        form.insertBefore(queueJson, fields);
        form.insertBefore(error, fields);
        form.insertBefore(casts, fields);

        fields.className = 'order-entry__grid order-entry__grid--modal';
        const itemPanel = document.createElement('section');
        itemPanel.className = 'slip-create__panel order-entry__items';
        const queuePanel = document.createElement('section');
        queuePanel.className = 'slip-create__panel order-entry__queue';
        const itemGroups = new Map();
        configuredOrderItems().forEach((item) => {
            const category = item.category || '未分類';
            const group = itemGroups.get(category) || [];
            group.push(item);
            itemGroups.set(category, group);
        });

        if (itemGroups.size === 0) {
            itemPanel.appendChild(Object.assign(document.createElement('p'), { className: 'text-muted mb-0', textContent: '注文可能な商品が未登録です。' }));
            submit.dataset.submitBaseDisabled = 'true';
            submit.disabled = true;
        } else {
            const tabs = document.createElement('div');
            tabs.className = 'order-category-tabs';
            tabs.setAttribute('role', 'tablist');
            tabs.setAttribute('aria-label', '商品カテゴリ');
            tabs.dataset.slipOrderCatalogTabs = '';
            const panels = document.createElement('div');
            panels.className = 'order-item-groups';
            Array.from(itemGroups.entries()).forEach(([category, items], index) => {
                const tab = document.createElement('button');
                tab.className = 'order-category-tab';
                tab.type = 'button';
                tab.dataset.slipOrderCatalogTab = String(index);
                tab.classList.toggle('is-active', index === 0);
                tab.textContent = category;
                tabs.appendChild(tab);

                const panel = document.createElement('section');
                panel.className = 'order-item-group';
                panel.dataset.slipOrderCatalogPanel = String(index);
                panel.classList.toggle('is-active', index === 0);
                const grid = document.createElement('div');
                grid.className = 'order-item-grid';
                items.forEach((item) => {
                    const button = document.createElement('button');
                    button.className = 'order-item-button';
                    button.type = 'button';
                    button.dataset.slipOrderCatalogItem = String(item.id);
                    button.dataset.slipOrderCatalogName = item.name || '';
                    button.dataset.slipOrderCatalogPrice = String(item.price ?? 0);
                    button.dataset.slipOrderCatalogCastBackTarget = item.isCastBackTarget ? 'true' : 'false';
                    button.append(
                        Object.assign(document.createElement('strong'), { textContent: item.name || '商品' }),
                        Object.assign(document.createElement('span'), { textContent: formatYen(item.price) })
                    );
                    grid.appendChild(button);
                });
                panel.appendChild(grid);
                panels.appendChild(panel);
            });
            itemPanel.append(tabs, panels);
            submit.dataset.submitBaseDisabled = 'false';
            submit.disabled = true;
        }

        const queueTitle = document.createElement('div');
        queueTitle.className = 'slip-create__panel-title';
        const queueHeading = document.createElement('h3');
        queueHeading.textContent = '注文キュー';
        const queueActions = document.createElement('div');
        queueActions.className = 'slip-create__panel-title-actions';
        const queueStatus = document.createElement('span');
        queueStatus.className = 'terminal-save-status';
        queueStatus.id = 'detailOrderQueueStatus';
        queueStatus.dataset.saveState = 'saved';
        queueStatus.setAttribute('aria-live', 'polite');
        queueStatus.textContent = '空';
        const clear = document.createElement('button');
        clear.className = 'btn btn-outline-secondary';
        clear.type = 'button';
        clear.id = 'detailClearQueueButton';
        clear.textContent = 'クリア';
        queueActions.append(queueStatus, clear);
        queueTitle.append(queueHeading, queueActions);
        const queueFields = document.createElement('div');
        queueFields.id = 'detailOrderQueueFields';
        const queueList = document.createElement('div');
        queueList.className = 'order-queue';
        queueList.id = 'detailOrderQueueList';
        const empty = document.createElement('div');
        empty.className = 'order-queue__empty';
        empty.id = 'detailOrderQueueEmpty';
        empty.textContent = '商品を選択してください。';
        const total = document.createElement('div');
        total.className = 'order-queue__total';
        total.append(
            Object.assign(document.createElement('span'), { textContent: '合計' }),
            Object.assign(document.createElement('strong'), { id: 'detailOrderQueueTotal', textContent: formatYen(0) })
        );
        queuePanel.append(queueTitle, queueFields, queueList, empty, total);
        fields.append(itemPanel, queuePanel);
        submit.id = 'detailSubmitOrderButton';
        return form;
    };

    const showLocalAction = (action, button) => {
        const manager = content.querySelector('[data-business-slip-editor-manager]');
        if (!manager) return;
        const recordId = button?.dataset.businessEditorRecordId || '';
        const recordLabel = button?.dataset.businessEditorRecordLabel || '';
        const recordDisplay = button?.dataset.businessEditorRecordDisplay || '';
        let form;

        if (action === 'customer_add') {
            const actionForm = createActionForm('客を追加', '追加');
            actionForm.fields.classList.add('business-slip-editor-action-fields--customer');
            const name = document.createElement('input');
            name.className = 'form-control form-control-lg';
            name.name = 'AddCustomersInput.CustomerLabels[0]';
            name.maxLength = 100;
            name.placeholder = '客名・特徴など';
            appendField(actionForm.fields, '客名', name).appendChild(Object.assign(document.createElement('div'), { className: 'form-text', textContent: '名前は空欄でも登録できます。' }));
            appendField(actionForm.fields, '入店時刻', createTimeSelect('AddCustomersInput.EnteredTime'));
            form = actionForm.form;
        } else if (action === 'customer_rename') {
            const actionForm = createActionForm(`${recordDisplay || '客名'}を変更`, '変更を保存');
            const customerId = document.createElement('input');
            customerId.type = 'hidden';
            customerId.name = 'UpdateCustomerInput.SlipCustomerId';
            customerId.value = recordId;
            const name = document.createElement('input');
            name.className = 'form-control form-control-lg';
            name.name = 'UpdateCustomerInput.CustomerLabel';
            name.maxLength = 100;
            name.value = recordLabel || recordDisplay;
            actionForm.form.insertBefore(customerId, actionForm.fields);
            appendField(actionForm.fields, '客名', name);
            form = actionForm.form;
        } else if (action === 'customer_leave') {
            const actionForm = createActionForm(`${recordDisplay || '客'}を退店`, '退店を保存');
            actionForm.submit.className = 'btn btn-outline-danger btn-lg align-self-end';
            const customerId = document.createElement('input');
            customerId.type = 'hidden';
            customerId.name = 'LeaveCustomerInput.SlipCustomerId';
            customerId.value = recordId;
            actionForm.form.insertBefore(customerId, actionForm.fields);
            appendField(actionForm.fields, '退店時刻', createTimeSelect('LeaveCustomerInput.LeftTime'));
            form = actionForm.form;
        } else if (action === 'nomination_add') {
            const actionForm = createActionForm('指名を追加', '追加');
            actionForm.fields.classList.add('business-slip-editor-action-fields--nomination');
            const kind = document.createElement('select');
            kind.className = 'form-select nomination-row__kind';
            kind.name = 'AddNominationsInput.CastNominations[0].NominationKind';
            kind.required = true;
            appendSelectOptions(kind, configuredNominationOptions(), configuredNominationOptions()[0]?.value, '指名区分');
            const price = document.createElement('select');
            price.className = 'form-select nomination-row__price';
            price.name = 'AddNominationsInput.CastNominations[0].NominationPrice';
            appendSelectOptions(price, configuredNominationPrices(), '1000');
            const cast = document.createElement('div');
            cast.className = 'nomination-row__cast';
            const castId = document.createElement('input');
            castId.type = 'hidden';
            castId.name = 'AddNominationsInput.CastNominations[0].CastId';
            castId.required = true;
            const castButton = document.createElement('button');
            castButton.className = 'selected-cast selected-cast--button';
            castButton.type = 'button';
            castButton.dataset.businessEditorOpenCastPicker = '';
            const castDisplay = document.createElement('span');
            castDisplay.dataset.businessEditorCastDisplay = '';
            castDisplay.textContent = 'キャストを選択';
            castButton.appendChild(castDisplay);
            cast.append(castId, castButton);
            const allowDuplicates = document.createElement('input');
            allowDuplicates.className = 'form-check-input';
            allowDuplicates.type = 'checkbox';
            allowDuplicates.id = 'businessEditorAllowNominationDuplicates';
            allowDuplicates.dataset.businessEditorAllowNominationDuplicates = '';
            const allowDuplicatesLabel = document.createElement('label');
            allowDuplicatesLabel.className = 'form-check-label';
            allowDuplicatesLabel.htmlFor = allowDuplicates.id;
            allowDuplicatesLabel.textContent = '重複あり';
            const duplicateField = document.createElement('div');
            duplicateField.className = 'form-check business-slip-editor-action-field--nomination-duplicate';
            duplicateField.append(allowDuplicates, allowDuplicatesLabel);
            allowDuplicates.addEventListener('change', () => {
                actionForm.form.querySelector('[data-business-editor-cast-error]')?.remove();
            });
            appendField(actionForm.fields, '指名区分', kind);
            appendField(actionForm.fields, '指名料金', price);
            appendField(actionForm.fields, 'キャスト', cast);
            actionForm.fields.appendChild(duplicateField);
            actionForm.submit.disabled = configuredNominationOptions().length === 0 || configuredCasts().length === 0;
            form = actionForm.form;
            syncCompanionPrice(kind);
        } else if (action === 'adjustment_add') {
            const actionForm = createActionForm('自由入力明細を追加', '追加');
            actionForm.fields.classList.add('business-slip-editor-action-fields--adjustment');
            actionForm.form.dataset.businessAdjustmentForm = '';
            const name = document.createElement('input');
            name.className = 'form-control form-control-lg';
            name.name = 'AdjustmentInput.LineName';
            name.maxLength = 160;
            name.placeholder = '例: 値引き';
            name.dataset.adjustmentName = '';
            const amount = document.createElement('input');
            amount.className = 'form-control form-control-lg';
            amount.name = 'AdjustmentInput.Amount';
            amount.type = 'number';
            amount.step = '1';
            amount.placeholder = '0';
            amount.dataset.adjustmentAmount = '';
            appendField(actionForm.fields, '明細名', name);
            appendField(actionForm.fields, '価格', amount).appendChild(Object.assign(document.createElement('small'), { className: 'text-muted', textContent: '値引きはマイナスで入力できます。' }));
            form = actionForm.form;
        } else if (action === 'order_add') {
            state.orderQueue.clear();
            state.pendingBackItemId = null;
            form = createOrderAddForm();
        }

        if (!form) return;
        modalElement.classList.toggle('is-order-editor', action === 'order_add');
        manager.replaceChildren(form);
        setFooterAction(form, form.querySelector('button[type="submit"]'));
        if (action === 'order_add') renderBusinessOrderQueue(form);
        form.querySelector('input:not([type="hidden"]), select')?.focus();
    };

    const showTemplateAction = (action, button) => {
        showLocalAction(action, button);
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
        const submit = document.createElement('button');
        submit.type = 'submit';
        submit.className = 'btn btn-danger btn-lg';
        submit.textContent = '削除する';
        form.append(slipId, titleRow, warning, submit);
        manager.replaceChildren(form);
        setFooterAction(form, submit);
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
            const castInput = form.querySelector('[name="AddNominationsInput.CastNominations[0].CastId"]');
            const kindSelect = form.querySelector('[name="AddNominationsInput.CastNominations[0].NominationKind"]');
            return {
                slipId,
                operationType: 'add_nomination',
                payload: {
                    cast_id: Number(value('AddNominationsInput.CastNominations[0].CastId')),
                    nomination_kind: value('AddNominationsInput.CastNominations[0].NominationKind'),
                    nomination_price: Number(value('AddNominationsInput.CastNominations[0].NominationPrice')),
                    allow_duplicate: allowsDuplicateNominations(form),
                    cast_display_name: form.querySelector('[data-business-editor-cast-display]')?.textContent?.trim() || castInput?.selectedOptions?.[0]?.textContent?.trim() || '',
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
        const nominationCastInput = form.querySelector('[name="AddNominationsInput.CastNominations[0].CastId"]');
        if (nominationCastInput && Number(nominationCastInput.value) <= 0) {
            const message = form.querySelector('[data-business-editor-cast-error]') || document.createElement('div');
            message.className = 'text-danger';
            message.dataset.businessEditorCastError = '';
            message.textContent = 'キャストを選択してください。';
            if (!message.parentElement) nominationCastInput.closest('.business-slip-editor-action-field')?.appendChild(message);
            return;
        }
        if (nominationCastInput && !allowsDuplicateNominations(form) && activeNominationCastIds().has(String(nominationCastInput.value))) {
            const message = form.querySelector('[data-business-editor-cast-error]') || document.createElement('div');
            message.className = 'text-danger';
            message.dataset.businessEditorCastError = '';
            message.textContent = 'このキャストは既に指名登録されています。重複ありをONにすると追加できます。';
            if (!message.parentElement) nominationCastInput.closest('.business-slip-editor-action-field')?.appendChild(message);
            return;
        }
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
        const nominationCastPicker = event.target.closest('[data-business-editor-open-cast-picker]');
        if (nominationCastPicker && content.contains(nominationCastPicker)) {
            event.preventDefault();
            openNominationCastPicker(nominationCastPicker.closest('[data-business-slip-editor-form]'));
            return;
        }

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
        modalElement.classList.remove('is-child-modal-active', 'is-order-editor');
        state.section = null;
        state.slipId = null;
        state.isSubmitting = false;
        state.orderQueue.clear();
        state.pendingBackItemId = null;
        state.pendingBackSelection.clear();
        content.replaceChildren();
    });

    attendingCastModalElement?.addEventListener('hidden.bs.modal', () => {
        modalElement.classList.remove('is-child-modal-active');
    });

    document.addEventListener('prosper:business-slips-updated', (event) => {
        if (!state.slipId || state.isSubmitting || !modalElement.classList.contains('show')) return;
        const refreshedSlip = event.detail?.slips?.find((slip) => String(slip.id) === state.slipId);
        if (!refreshedSlip || refreshedSlip.status !== 'open') {
            renderUnavailable('この伝票は会計準備中または会計済みのため編集できません。営業中一覧を再表示してください。');
        }
    });
})();
