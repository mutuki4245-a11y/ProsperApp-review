(() => {
    const root = document.querySelector('[data-drink-delivery-editor]');
    if (!root) {
        return;
    }

    const departmentId = Number(root.dataset.departmentId) || 0;
    const storageKey = `prosper:drink-delivery-editor:${departmentId}`;
    const commandStorageKey = `prosper:drink-delivery-mutation:v2:${departmentId}`;
    const form = root.querySelector('[data-drink-form]');
    const amountInput = root.querySelector('[data-drink-amount]');
    const submit = root.querySelector('[data-drink-submit]');
    const status = root.querySelector('[data-drink-status]');
    const error = root.querySelector('[data-drink-error]');
    const success = root.querySelector('[data-drink-success]');
    const updated = root.querySelector('[data-drink-updated]');
    const guidance = root.querySelector('[data-drink-guidance]');
    const businessDate = root.querySelector('[data-drink-business-date]');
    const saveResponse = window.ProsperSaveResponse;
    let editor = null;
    let pendingCommand = null;
    let saving = false;

    const setMessage = (message, state = 'idle') => {
        if (status) {
            status.textContent = message;
            status.dataset.saveState = state;
        }
    };

    const showError = (message) => {
        if (error) {
            error.textContent = message;
            error.hidden = false;
        }
        if (success) {
            success.hidden = true;
        }
        setMessage(message, 'error');
    };

    const clearMessages = () => {
        if (error) {
            error.hidden = true;
        }
        if (success) {
            success.hidden = true;
        }
    };
    const readAmount = () => Number(String(amountInput?.value ?? '').replace(/,/g, '').trim());

    const normalizeEditor = (value) => ({
        departmentId: Number(value?.departmentId) || departmentId,
        hasBusinessDay: Boolean(value?.hasBusinessDay),
        businessDayId: value?.businessDayId == null ? null : Number(value.businessDayId),
        businessDayRevision: Number(value?.businessDayRevision) || 0,
        businessDate: value?.businessDate || null,
        drinkDeliveryAmount: Number(value?.drinkDeliveryAmount) || 0,
        isEntered: Boolean(value?.isEntered),
        checkedAt: value?.checkedAt || new Date().toISOString()
    });

    const persistEditor = (value) => {
        try {
            sessionStorage.setItem(storageKey, JSON.stringify(value));
        } catch {
            // A later direct visit can recover through the editor read endpoint.
        }
    };
    const readPendingCommand = () => {
        try {
            const command = JSON.parse(sessionStorage.getItem(commandStorageKey) || 'null');
            return command?.operationId ? command : null;
        } catch {
            return null;
        }
    };
    const persistPendingCommand = () => {
        try {
            if (pendingCommand) sessionStorage.setItem(commandStorageKey, JSON.stringify(pendingCommand));
            else sessionStorage.removeItem(commandStorageKey);
        } catch {
            // The command remains available in memory for this page lifetime.
        }
    };
    const clearPendingCommand = () => {
        pendingCommand = null;
        persistPendingCommand();
    };

    const applyEditor = (value, options = {}) => {
        editor = normalizeEditor(value);
        persistEditor(editor);
        if (!options.preserveAmount && !pendingCommand && amountInput) {
            amountInput.value = String(editor.drinkDeliveryAmount);
        }

        if (amountInput) {
            amountInput.disabled = false;
        }
        if (submit) {
            submit.disabled = false;
        }
        if (businessDate) {
            businessDate.textContent = editor.hasBusinessDay && editor.businessDate
                ? editor.businessDate
                : `営業日 ${root.dataset.currentBusinessDate} / 保存時に自動作成`;
        }
        if (updated) {
            const checkedAt = new Date(editor.checkedAt);
            updated.textContent = Number.isNaN(checkedAt.getTime())
                ? '状態を復元しました。'
                : `最終確認: ${checkedAt.toLocaleString('ja-JP')}`;
        }
        if (guidance) {
            guidance.classList.toggle('alert-warning', editor.hasBusinessDay && !editor.isEntered);
            guidance.classList.toggle('alert-info', !editor.hasBusinessDay || editor.isEntered);
            guidance.textContent = editor.hasBusinessDay && !editor.isEntered
                ? '納品額が未入力です。酒代がない場合も 0 円で保存してください。'
                : editor.hasBusinessDay
                    ? '保存済みの納品額を修正できます。'
                    : `保存すると営業日 ${root.dataset.currentBusinessDate} を自動作成します。酒代がない場合も 0 円で保存してください。`;
        }
        setMessage(editor.isEntered ? '保存済みです。' : '入力できます。', editor.isEntered ? 'done' : 'idle');
    };

    const restoreEditor = () => {
        try {
            const raw = sessionStorage.getItem(storageKey);
            if (!raw) {
                return false;
            }

            const stored = JSON.parse(raw);
            if (Number(stored?.departmentId) !== departmentId ||
                typeof stored?.businessDayRevision !== 'number') {
                return false;
            }

            applyEditor(stored);
            return true;
        } catch {
            return false;
        }
    };

    const loadEditor = async () => {
        try {
            const response = await fetch(root.dataset.editorUrl, {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || result?.succeeded !== true || !result.editor) {
                throw new Error(result?.errorMessage || '納品額入力状況を取得できませんでした。');
            }

            applyEditor(result.editor);
        } catch (loadError) {
            showError(loadError.message);
            if (amountInput) {
                amountInput.disabled = true;
            }
            if (submit) {
                submit.disabled = true;
            }
        }
    };

    const buildCommand = () => ({
        operationId: crypto.randomUUID(),
        expectedBusinessDayId: editor?.hasBusinessDay ? editor.businessDayId : null,
        expectedBusinessDayRevision: editor?.hasBusinessDay ? editor.businessDayRevision : null,
        drinkDeliveryAmount: readAmount()
    });

    form?.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (saving || !editor) {
            return;
        }

        const amount = readAmount();
        if (!Number.isInteger(amount) || amount < 0 || amount > 999999999999) {
            showError('納品額は0円以上の整数で入力してください。');
            amountInput?.focus();
            return;
        }

        clearMessages();
        if (!pendingCommand) {
            pendingCommand = buildCommand();
            persistPendingCommand();
        }
        saving = true;
        if (submit) {
            submit.disabled = true;
            submit.textContent = '保存中';
        }
        setMessage('保存しています。', 'saving');

        try {
            const token = form.querySelector('[name="__RequestVerificationToken"]')?.value;
            const response = await fetch(root.dataset.saveUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/json',
                    RequestVerificationToken: token || ''
                },
                body: JSON.stringify(pendingCommand)
            });
            const result = await response.json().catch(() => null);
            const classification = saveResponse?.classify({ response, payload: result }) ?? {
                confirmed: result?.status === 'confirmed',
                rejected: ['conflict', 'validation_error', 'permission_denied', 'stale_work_item'].includes(result?.status),
                retry: !result || response.status >= 500 || result?.status === 'unavailable'
            };
            const definitive = classification.rejected;
            if (result?.editor) {
                applyEditor(result.editor, { preserveAmount: definitive });
            }

            if (!classification.confirmed) {
                if (definitive) {
                    clearPendingCommand();
                }
                throw new Error(result?.message || '納品額を保存できませんでした。');
            }

            clearPendingCommand();
            if (success) {
                success.textContent = `納品額 ${Math.trunc(Number(result.editor.drinkDeliveryAmount) || 0).toLocaleString('ja-JP')}円を保存しました。`;
                success.hidden = false;
            }
            setMessage('保存しました。', 'done');
            if (result.dashboardDelta) {
                window.dispatchEvent(new CustomEvent('prosper:closing-dashboard-delta', {
                    detail: result.dashboardDelta
                }));
            }
        } catch (saveError) {
            if (pendingCommand && amountInput) {
                amountInput.value = String(pendingCommand.drinkDeliveryAmount);
            }
            showError(saveError.message);
        } finally {
            saving = false;
            if (submit) {
                submit.disabled = false;
                submit.textContent = '保存';
            }
        }
    });

    amountInput?.addEventListener('input', () => {
        if (!pendingCommand || saving || readAmount() === pendingCommand.drinkDeliveryAmount) {
            return;
        }

        amountInput.value = String(pendingCommand.drinkDeliveryAmount);
        setMessage('前回の保存結果を同じ操作IDで確認してから、金額を変更できます。', 'error');
    });

    pendingCommand = readPendingCommand();
    if (pendingCommand && amountInput) {
        amountInput.value = String(pendingCommand.drinkDeliveryAmount);
    }
    if (!restoreEditor()) {
        void loadEditor();
    }
})();
