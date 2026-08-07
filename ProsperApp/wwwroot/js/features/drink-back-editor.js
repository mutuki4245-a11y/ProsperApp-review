(() => {
    const root = document.querySelector('[data-drink-back-editor]');
    if (!root) {
        return;
    }

    const departmentId = Number(root.dataset.departmentId) || 0;
    const editorStorageKey = `prosper:drink-back-editor:${departmentId}`;
    const pendingStorageKey = `prosper:drink-back-command:${departmentId}:v2`;
    const castMasterStore = window.ProsperSync
        ? window.ProsperSync.getStore(`closing-cast-master:${departmentId}:v1`)
        : null;
    const form = root.querySelector('[data-drink-back-form]');
    const requiredRows = root.querySelector('[data-drink-back-required-rows]');
    const optionalRows = root.querySelector('[data-drink-back-optional-rows]');
    const optionalSection = root.querySelector('[data-drink-back-optional-section]');
    const optionalToggle = root.querySelector('[data-drink-back-show-optional]');
    const optionalBadge = root.querySelector('[data-drink-back-optional-badge]');
    const requiredStatus = root.querySelector('[data-drink-back-required-status]');
    const total = root.querySelector('[data-drink-back-total]');
    const businessDate = root.querySelector('[data-drink-back-business-date]');
    const guidance = root.querySelector('[data-drink-back-guidance]');
    const status = root.querySelector('[data-drink-back-status]');
    const updated = root.querySelector('[data-drink-back-updated]');
    const error = root.querySelector('[data-drink-back-error]');
    const success = root.querySelector('[data-drink-back-success]');
    const submit = root.querySelector('[data-drink-back-submit]');
    const amountLimit = 999999999999;
    let editor = null;
    let activeCasts = [];
    let pendingCommand = null;
    let draftCommand = null;
    let saving = false;

    try {
        const storedCommand = JSON.parse(sessionStorage.getItem(pendingStorageKey) || 'null');
        if (storedCommand?.operationId) pendingCommand = storedCommand;
    } catch {
        sessionStorage.removeItem(pendingStorageKey);
    }

    const persistPendingCommand = () => {
        try {
            if (pendingCommand) sessionStorage.setItem(pendingStorageKey, JSON.stringify(pendingCommand));
            else sessionStorage.removeItem(pendingStorageKey);
        } catch {
            // The in-memory command still preserves the operation ID for this page lifetime.
        }
    };

    const toYen = (value) => `${Math.trunc(Number(value) || 0).toLocaleString('ja-JP')}円`;
    const setStatus = (message, state = 'idle') => {
        if (status) {
            status.textContent = message;
            status.dataset.saveState = state;
        }
    };
    const clearMessages = () => {
        if (error) error.hidden = true;
        if (success) success.hidden = true;
    };
    const showError = (message) => {
        if (error) {
            error.textContent = message;
            error.hidden = false;
        }
        if (success) success.hidden = true;
        setStatus(message, 'error');
    };
    const friendlyMessage = (message) => ({
        business_day_revision_conflict: '営業日または出勤キャストが更新されています。入力内容を確認して再度保存してください。',
        invalid_drink_back_payload: '調整額は符号付き整数円で入力してください。',
        duplicate_drink_back_cast: '同じキャストが複数の保存対象に含まれています。',
        drink_back_required_casts_changed: '出勤キャストが更新されています。入力内容を確認してください。',
        drink_back_optional_cast_is_attending: '出勤状態が更新されたキャストがいます。入力内容を確認してください。',
        drink_back_cast_not_active: '店舗で有効なキャストだけを調整できます。',
        drink_back_remove_target_not_found: '削除対象の調整行が更新されています。'
    }[message] || message);

    const normalizeRow = (value, required) => ({
        castId: Number(value?.castId) || 0,
        displayName: String(value?.displayName || ''),
        departmentName: value?.departmentName || null,
        adjustmentAmount: value?.adjustmentAmount == null ? null : Number(value.adjustmentAmount),
        isSaved: Boolean(value?.isSaved),
        isRequired: required
    });
    const normalizeEditor = (value) => ({
        departmentId: Number(value?.departmentId) || departmentId,
        hasBusinessDay: Boolean(value?.hasBusinessDay),
        businessDayId: value?.businessDayId == null ? null : Number(value.businessDayId),
        businessDayRevision: Number(value?.businessDayRevision) || 0,
        businessDate: value?.businessDate || null,
        requiredRows: Array.isArray(value?.requiredRows)
            ? value.requiredRows.map((row) => normalizeRow(row, true)).filter((row) => row.castId > 0)
            : [],
        optionalRows: Array.isArray(value?.optionalRows)
            ? value.optionalRows.map((row) => normalizeRow(row, false)).filter((row) => row.castId > 0)
            : [],
        status: {
            requiredCastCount: Number(value?.status?.requiredCastCount) || 0,
            completedCastCount: Number(value?.status?.completedCastCount) || 0,
            missingCastCount: Number(value?.status?.missingCastCount) || 0,
            totalAmount: Number(value?.status?.totalAmount) || 0
        },
        optionalAdjustmentCount: Number(value?.optionalAdjustmentCount) || 0,
        checkedAt: value?.checkedAt || new Date().toISOString()
    });
    const normalizeCast = (value) => ({
        castId: Number(value?.castId) || 0,
        displayName: String(value?.displayName || ''),
        departmentName: value?.departmentName || null
    });
    const displayName = (value) => value.departmentName
        ? `${value.displayName}：${value.departmentName}`
        : value.displayName;

    const persistEditor = () => {
        try {
            sessionStorage.setItem(editorStorageKey, JSON.stringify(editor));
        } catch {
            // A direct visit can recover with the single editor read.
        }
    };
    const persistActiveCasts = (casts, revision) => {
        if (!castMasterStore || !revision || !Array.isArray(casts)) {
            return;
        }
        castMasterStore.replace({ departmentId, casts }, revision, { force: true });
    };

    const makeAmountInput = (row, required, selected) => {
        const wrapper = document.createElement('label');
        wrapper.className = 'drink-back-editor__row';
        wrapper.dataset.castId = String(row.castId);
        wrapper.dataset.required = String(required);

        const identity = document.createElement('span');
        identity.className = 'drink-back-editor__identity';
        const name = document.createElement('strong');
        name.textContent = displayName(row);
        identity.append(name);
        if (!required) {
            const selector = document.createElement('input');
            selector.type = 'checkbox';
            selector.className = 'form-check-input';
            selector.checked = selected;
            selector.dataset.optionalSelected = '';
            selector.setAttribute('aria-label', `${displayName(row)}を調整対象にする`);
            identity.prepend(selector);
        } else {
            const badge = document.createElement('span');
            badge.className = `badge ${row.isSaved ? 'text-bg-success' : 'text-bg-warning'}`;
            badge.textContent = row.isSaved ? '保存済み' : '未保存';
            identity.append(badge);
        }

        const amount = document.createElement('span');
        amount.className = 'closing-amount-input';
        const input = document.createElement('input');
        input.className = 'form-control';
        input.type = 'number';
        input.step = '1';
        input.min = String(-amountLimit);
        input.max = String(amountLimit);
        input.value = String(row.adjustmentAmount ?? 0);
        input.disabled = !required && !selected;
        input.dataset.adjustmentAmount = '';
        input.setAttribute('aria-label', `${displayName(row)}のドリンクバック調整額`);
        const unit = document.createElement('span');
        unit.textContent = '円';
        amount.append(input, unit);
        wrapper.append(identity, amount);

        if (!required) {
            const selector = identity.querySelector('[data-optional-selected]');
            selector?.addEventListener('change', () => {
                input.disabled = !selector.checked;
                if (selector.checked) input.focus();
            });
        }
        return wrapper;
    };

    const draftAmount = (collection, castId, fallback) => {
        const found = collection?.find((item) => Number(item.castId) === Number(castId));
        return found ? Number(found.adjustmentAmount) : fallback;
    };
    const render = () => {
        if (!editor) return;
        const requiredDrafts = draftCommand?.requiredAdjustments || [];
        const optionalDrafts = draftCommand?.optionalAdjustments || [];
        const removedDraftIds = new Set((draftCommand?.removeCastIds || []).map(Number));
        requiredRows.replaceChildren(...editor.requiredRows.map((row) => makeAmountInput({
            ...row,
            adjustmentAmount: draftAmount(requiredDrafts, row.castId, row.adjustmentAmount)
        }, true, true)));

        const requiredIds = new Set(editor.requiredRows.map((row) => row.castId));
        const existingById = new Map(editor.optionalRows.map((row) => [row.castId, row]));
        const candidates = new Map(activeCasts
            .filter((cast) => !requiredIds.has(cast.castId))
            .map((cast) => [cast.castId, { ...cast, adjustmentAmount: null, isSaved: false }]));
        editor.optionalRows.forEach((row) => candidates.set(row.castId, row));
        const optionalNodes = Array.from(candidates.values())
            .sort((left, right) => displayName(left).localeCompare(displayName(right), 'ja'))
            .map((row) => {
                const draft = optionalDrafts.find((item) => Number(item.castId) === row.castId);
                const selected = draft
                    ? true
                    : removedDraftIds.has(row.castId)
                        ? false
                        : row.isSaved;
                return makeAmountInput({
                    ...row,
                    adjustmentAmount: draft ? Number(draft.adjustmentAmount) : row.adjustmentAmount
                }, false, selected);
            });
        optionalRows.replaceChildren(...optionalNodes);

        const savedOptionalCount = editor.optionalRows.length;
        optionalBadge.textContent = `保存済み ${savedOptionalCount}名`;
        optionalBadge.className = `badge ${savedOptionalCount > 0 ? 'text-bg-warning' : 'text-bg-secondary'}`;
        requiredStatus.textContent = editor.status.missingCastCount > 0
            ? `未入力 ${editor.status.missingCastCount}名`
            : `必須 ${editor.status.completedCastCount}/${editor.status.requiredCastCount}名 保存済み`;
        total.textContent = `合計 ${toYen(editor.status.totalAmount)}`;
        businessDate.textContent = editor.hasBusinessDay && editor.businessDate
            ? `${editor.businessDate} / 出勤キャスト ${editor.status.requiredCastCount}名`
            : `営業日 ${root.dataset.currentBusinessDate} / 勤怠保存待ち`;
        guidance.textContent = !editor.hasBusinessDay
            ? '営業日と勤怠を保存してから調整します。'
            : editor.status.requiredCastCount === 0
                ? '出勤キャストが登録されていません。'
                : editor.status.missingCastCount > 0
                    ? `未入力の出勤キャストが ${editor.status.missingCastCount}名います。`
                    : '保存済みの調整額を変更できます。';
        guidance.className = `alert ${editor.hasBusinessDay && editor.status.requiredCastCount > 0 ? 'alert-info' : 'alert-warning'}`;
        const checkedAt = new Date(editor.checkedAt);
        updated.textContent = Number.isNaN(checkedAt.getTime()) ? '' : `最終確認: ${checkedAt.toLocaleString('ja-JP')}`;
        submit.disabled = !editor.hasBusinessDay || editor.status.requiredCastCount === 0 || saving;
        setStatus(editor.status.missingCastCount === 0 ? '保存済みです。' : '入力できます。', editor.status.missingCastCount === 0 ? 'done' : 'idle');
    };

    const applyState = (editorValue, casts, castRevision, options = {}) => {
        editor = normalizeEditor(editorValue);
        if (Array.isArray(casts)) {
            activeCasts = casts.map(normalizeCast).filter((cast) => cast.castId > 0 && cast.displayName);
            persistActiveCasts(activeCasts, castRevision);
        }
        persistEditor();
        if (!options.preserveDraft) draftCommand = null;
        render();
    };

    const restore = () => {
        try {
            const storedEditor = JSON.parse(sessionStorage.getItem(editorStorageKey) || 'null');
            const storedMaster = castMasterStore?.get();
            const storedCasts = storedMaster?.payload?.casts;
            if (!storedEditor || Number(storedEditor.departmentId) !== departmentId || !Array.isArray(storedCasts)) {
                return false;
            }
            applyState(storedEditor, storedCasts, storedMaster.revision);
            if (pendingCommand) {
                draftCommand = pendingCommand;
                render();
                queueMicrotask(() => form?.requestSubmit());
            }
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
            if (!response.ok || result?.succeeded !== true || !result.editor || !Array.isArray(result.activeCasts)) {
                throw new Error(result?.message || 'ドリンクバック調整を取得できませんでした。');
            }
            applyState(result.editor, result.activeCasts, result.castMasterRevision);
            if (pendingCommand) {
                draftCommand = pendingCommand;
                render();
                queueMicrotask(() => form?.requestSubmit());
            }
        } catch (loadError) {
            showError(loadError.message);
            submit.disabled = true;
        }
    };

    const parseAmount = (input) => {
        const value = Number(input.value);
        return Number.isSafeInteger(value) && Math.abs(value) <= amountLimit ? value : null;
    };
    const collectCommand = () => {
        const requiredAdjustments = Array.from(requiredRows.querySelectorAll('[data-cast-id]')).map((row) => ({
            castId: Number(row.dataset.castId),
            adjustmentAmount: parseAmount(row.querySelector('[data-adjustment-amount]'))
        }));
        if (requiredAdjustments.some((item) => item.adjustmentAmount == null)) {
            throw new Error('出勤キャスト全員の調整額を符号付き整数円で入力してください。');
        }

        const optionalAdjustments = [];
        const removeCastIds = [];
        const existingOptionalIds = new Set(editor.optionalRows.map((row) => row.castId));
        optionalRows.querySelectorAll('[data-cast-id]').forEach((row) => {
            const castId = Number(row.dataset.castId);
            const selected = row.querySelector('[data-optional-selected]')?.checked === true;
            if (!selected) {
                if (existingOptionalIds.has(castId)) removeCastIds.push(castId);
                return;
            }
            const adjustmentAmount = parseAmount(row.querySelector('[data-adjustment-amount]'));
            if (adjustmentAmount == null) {
                throw new Error('選択した出勤外キャストの調整額を符号付き整数円で入力してください。');
            }
            optionalAdjustments.push({ castId, adjustmentAmount });
        });
        return {
            operationId: crypto.randomUUID(),
            expectedBusinessDayId: editor.businessDayId,
            expectedBusinessDayRevision: editor.businessDayRevision,
            requiredAdjustments,
            optionalAdjustments,
            removeCastIds
        };
    };

    form?.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (saving || !editor) return;
        clearMessages();
        try {
            pendingCommand ??= collectCommand();
            persistPendingCommand();
        } catch (validationError) {
            showError(validationError.message);
            return;
        }

        saving = true;
        submit.disabled = true;
        submit.textContent = '保存中';
        setStatus('保存しています。', 'saving');
        try {
            const token = form.querySelector('[name="__RequestVerificationToken"]')?.value || '';
            const response = await fetch(root.dataset.saveUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/json',
                    RequestVerificationToken: token
                },
                body: JSON.stringify(pendingCommand)
            });
            const result = await response.json().catch(() => null);
            const definitive = result?.status === 'conflict' || result?.status === 'validation_error';
            if (definitive && result?.editor) {
                draftCommand = pendingCommand;
                pendingCommand = null;
                persistPendingCommand();
                applyState(result.editor, null, null, { preserveDraft: true });
            }
            if (!response.ok || result?.status !== 'confirmed') {
                throw new Error(friendlyMessage(result?.message) || (definitive
                    ? '最新状態を反映しました。入力内容を確認して再度保存してください。'
                    : 'ドリンクバック調整を保存できませんでした。'));
            }

            pendingCommand = null;
            persistPendingCommand();
            applyState(result.editor, null, null);
            if (success) {
                success.textContent = `ドリンクバック調整（合計 ${toYen(result.drinkBackStatus?.totalAmount)}）を保存しました。`;
                success.hidden = false;
            }
            setStatus('保存しました。', 'done');
            if (result.dashboardDelta) {
                window.dispatchEvent(new CustomEvent('prosper:closing-dashboard-delta', {
                    detail: result.dashboardDelta
                }));
            }
        } catch (saveError) {
            showError(saveError.message);
            if (pendingCommand) {
                draftCommand = pendingCommand;
                render();
            }
        } finally {
            saving = false;
            submit.disabled = !editor?.hasBusinessDay || editor?.status.requiredCastCount === 0;
            submit.textContent = '保存';
        }
    });

    form?.addEventListener('input', () => {
        if (!pendingCommand || saving) return;
        draftCommand = pendingCommand;
        render();
        setStatus('前回の保存結果を同じ操作IDで確認してから内容を変更できます。', 'error');
    });
    optionalToggle?.addEventListener('change', () => {
        optionalSection.hidden = !optionalToggle.checked;
    });

    if (!restore()) {
        void loadEditor();
    }
})();
