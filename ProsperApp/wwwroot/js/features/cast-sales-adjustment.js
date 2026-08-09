(() => {
    const root = document.querySelector('[data-cast-sales-editor]');
    if (!root) {
        return;
    }

    const overviewUrl = root.dataset.overviewUrl;
    const saveUrl = root.dataset.saveUrl;
    const confirmUrl = root.dataset.confirmUrl;
    const summary = root.querySelector('[data-cast-sales-summary]');
    const error = root.querySelector('[data-cast-sales-error]');
    const errorMessage = root.querySelector('[data-cast-sales-error-message]');
    const success = root.querySelector('[data-cast-sales-success]');
    const updated = root.querySelector('[data-cast-sales-updated]');
    const loading = root.querySelector('[data-cast-sales-loading]');
    const empty = root.querySelector('[data-cast-sales-empty]');
    const emptyTitle = root.querySelector('[data-cast-sales-empty-title]');
    const emptyMessage = root.querySelector('[data-cast-sales-empty-message]');
    const list = root.querySelector('[data-cast-sales-list]');
    const actions = root.querySelector('[data-cast-sales-actions]');
    const missing = root.querySelector('[data-cast-sales-missing]');
    const confirmButton = root.querySelector('[data-cast-sales-confirm]');
    const status = root.querySelector('[data-cast-sales-status]');
    const retryButton = root.querySelector('[data-cast-sales-retry]');
    const modalElement = document.getElementById('castSalesAdjustmentModal');
    const modal = modalElement && window.bootstrap
        ? window.bootstrap.Modal.getOrCreateInstance(modalElement)
        : null;
    const form = root.querySelector('[data-cast-sales-form]');
    const baseLabel = root.querySelector('[data-cast-sales-base]');
    const guidance = root.querySelector('[data-cast-sales-guidance]');
    const castRows = root.querySelector('[data-cast-sales-casts]');
    const saveButton = root.querySelector('[data-cast-sales-save]');
    const antiForgeryToken = root.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const saveResponse = window.ProsperSaveResponse;
    const yenFormatter = new Intl.NumberFormat('ja-JP', {
        style: 'currency',
        currency: 'JPY',
        maximumFractionDigits: 0
    });

    let overview = null;
    let activeDetail = null;
    let loadPromise = null;
    let pendingMutation = null;
    const pendingStorageKey = `prosper:cast-sales-mutation:v2:${root.dataset.departmentId || 'current'}`;
    const readPendingMutation = () => {
        try {
            const value = JSON.parse(sessionStorage.getItem(pendingStorageKey) || 'null');
            return value?.kind && value?.command?.operationId ? value : null;
        } catch {
            return null;
        }
    };
    const persistPendingMutation = () => {
        try {
            if (pendingMutation) sessionStorage.setItem(pendingStorageKey, JSON.stringify(pendingMutation));
            else sessionStorage.removeItem(pendingStorageKey);
        } catch {
            // The in-memory command remains available for this page lifetime.
        }
    };
    const clearPendingMutation = () => {
        pendingMutation = null;
        persistPendingMutation();
    };
    pendingMutation = readPendingMutation();

    const createOperationId = () => {
        if (window.crypto?.randomUUID) {
            return window.crypto.randomUUID();
        }

        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (value) => {
            const random = Math.floor(Math.random() * 16);
            const output = value === 'x' ? random : (random & 0x3) | 0x8;
            return output.toString(16);
        });
    };

    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text != null) {
            node.textContent = text;
        }
        return node;
    };

    const setStatus = (message, state = 'idle') => {
        status.textContent = message;
        status.dataset.saveState = state;
    };

    const showError = (message, allowRetry = false) => {
        errorMessage.textContent = message;
        retryButton.hidden = !allowRetry;
        error.hidden = false;
    };

    const clearMessages = () => {
        error.hidden = true;
        success.hidden = true;
    };

    const showSuccess = (message) => {
        success.textContent = message;
        success.hidden = false;
    };

    const formatTime = (value) => {
        const date = new Date(value);
        return Number.isNaN(date.getTime())
            ? '-'
            : new Intl.DateTimeFormat('ja-JP', { hour: '2-digit', minute: '2-digit' }).format(date);
    };

    const fallbackMessage = (reason) => ({
        missing_nomination_start_time: '指名開始時刻が不足しているため、均等配分を初期表示しています。',
        checkout_snapshot_mismatch: '会計額と明細の整合性を確認できないため、均等配分を初期表示しています。',
        unallocated_sales_event: '指名開始前の売上があるため、均等配分を初期表示しています。',
        negative_cast_sales_amount: '値引きによりキャスト別売上額が負になるため、均等配分を初期表示しています。'
    }[reason] ?? '売上発生時点の配分を計算できないため、均等配分を初期表示しています。');

    const initialValues = (detail) => {
        const rows = detail.casts ?? [];
        if (rows.length === 0) {
            return { values: [], guidance: '' };
        }

        const baseAmount = Math.trunc(Number(
            overview.sourceAmountType === 'subtotal' ? detail.subtotalAmount : detail.totalAmount) || 0);
        if (overview.splitMode === 'full') {
            return {
                values: rows.map((row) => row.salesAmount ?? baseAmount),
                guidance: '各キャストに配分基準額を設定します。'
            };
        }

        const suggestedKey = overview.sourceAmountType === 'subtotal'
            ? 'suggestedSubtotalSalesAmount'
            : 'suggestedTotalSalesAmount';
        const fallbackKey = overview.sourceAmountType === 'subtotal'
            ? 'subtotalSuggestionFallbackReason'
            : 'totalSuggestionFallbackReason';
        const fallbackReason = rows.map((row) => row[fallbackKey]).find(Boolean);
        const suggested = rows.map((row) => row[suggestedKey]);
        let defaults;
        let message;
        if (!fallbackReason && suggested.every((value) => value != null)) {
            defaults = suggested.map(Number);
            message = '売上発生時点で有効な指名キャストへ自動配分しています。';
        } else {
            const divided = Math.trunc(baseAmount / rows.length);
            const remainder = baseAmount % rows.length;
            defaults = rows.map((_, index) => divided + (index < remainder ? 1 : 0));
            message = fallbackMessage(fallbackReason);
        }

        return {
            values: rows.map((row, index) => row.salesAmount ?? defaults[index]),
            guidance: rows.every((row) => row.salesAmount == null) ? message : '保存済みの配分額を表示しています。'
        };
    };

    const detailFor = (slipId) => overview?.details?.find((detail) => Number(detail.slipId) === Number(slipId));

    const render = () => {
        loading.hidden = true;
        list.replaceChildren();
        if (!overview?.hasBusinessDay) {
            summary.textContent = '営業中の営業日はありません';
            emptyTitle.textContent = '調整対象はありません';
            emptyMessage.textContent = '営業日が作成されると、会計済み伝票の売上額調整を確認できます。';
            empty.hidden = false;
            list.hidden = true;
            actions.hidden = true;
            updated.textContent = overview?.checkedAt ? `最終更新: ${new Date(overview.checkedAt).toLocaleString('ja-JP')}` : '';
            setStatus('営業日の開始を待っています。');
            return;
        }

        const slips = overview.slips ?? [];
        const targetCount = Number(overview.status?.requiredSlipCount ?? 0);
        summary.textContent = `${overview.businessDate} / 対象 ${targetCount} 件`;
        updated.textContent = overview.checkedAt ? `最終更新: ${new Date(overview.checkedAt).toLocaleString('ja-JP')}` : '';
        if (slips.length === 0) {
            emptyTitle.textContent = '調整対象の伝票はありません';
            emptyMessage.textContent = '会計済みかつ指名キャストがいる伝票が対象です。';
            empty.hidden = false;
            list.hidden = true;
            actions.hidden = true;
            setStatus('調整対象はありません。');
            return;
        }

        empty.hidden = true;
        list.hidden = false;
        for (const slip of slips) {
            const detail = detailFor(slip.slipId);
            const row = element('article', 'cast-sales-editor__slip');
            const main = element('div', 'cast-sales-editor__slip-main');
            main.append(
                element('strong', null, slip.tableDisplayName ?? '-'),
                element('small', null, `${formatTime(slip.checkoutAt)} / ${slip.customerDisplayNames ?? 'お客様名なし'}`));

            const castSummary = element('div', 'cast-sales-editor__cast-summary');
            if (detail) {
                const defaults = initialValues(detail).values;
                detail.casts.forEach((cast, index) => {
                    const castLine = element('span');
                    castLine.append(
                        element('strong', null, cast.castDisplayName),
                        element('small', null, `${cast.nominationDisplayName} / ${yenFormatter.format(defaults[index])}`));
                    castSummary.append(castLine);
                });
            } else {
                castSummary.append(element('small', null, '最新情報を取得できませんでした。'));
            }

            const amount = element('div', 'cast-sales-editor__slip-amount');
            amount.append(
                element('strong', null, yenFormatter.format(Number(slip.totalAmount) || 0)),
                element('small', 'd-block text-muted', slip.isAdjusted ? '調整済み' : '未調整'));
            const action = element('div', 'cast-sales-editor__slip-action');
            const button = element('button', 'btn btn-primary btn-sm', '売上額調整');
            button.type = 'button';
            button.disabled = !detail;
            button.addEventListener('click', () => openEditor(detail));
            action.append(button);
            row.append(main, castSummary, amount, action);
            list.append(row);
        }

        const missingCount = Number(overview.status?.missingSlipCount ?? 0);
        actions.hidden = false;
        missing.textContent = missingCount > 0 ? `未調整 ${missingCount} 件` : '全件調整済み';
        confirmButton.disabled = pendingMutation != null;
        setStatus('調整内容を確認できます。');
    };

    const openEditor = (detail) => {
        if (!detail) {
            return;
        }

        clearMessages();
        activeDetail = detail;
        const initial = initialValues(detail);
        const baseAmount = overview.sourceAmountType === 'subtotal' ? detail.subtotalAmount : detail.totalAmount;
        baseLabel.textContent = `配分基準 ${yenFormatter.format(Number(baseAmount) || 0)}`;
        guidance.textContent = initial.guidance;
        castRows.replaceChildren();
        detail.casts.forEach((cast, index) => {
            const row = element('div', 'cast-sales-editor__cast-row');
            row.dataset.slipCastId = String(cast.slipCastId);
            const amount = element('label', 'cast-sales-editor__amount-input');
            const input = element('input', 'form-control');
            input.type = 'number';
            input.min = '0';
            input.max = '999999999999';
            input.step = '1';
            input.inputMode = 'numeric';
            input.required = true;
            input.value = String(initial.values[index]);
            amount.append(input, element('span', null, '円'));
            row.append(
                element('strong', null, cast.castDisplayName),
                element('span', null, cast.nominationDisplayName),
                amount);
            castRows.append(row);
        });
        setEditorDisabled(false);
        modal?.show();
    };

    const setEditorDisabled = (disabled) => {
        castRows.querySelectorAll('input').forEach((input) => { input.disabled = disabled; });
        modalElement?.querySelectorAll('[data-bs-dismiss="modal"]').forEach((button) => {
            button.disabled = disabled;
        });
        saveButton.disabled = disabled;
    };

    const readAdjustments = () => {
        const values = [];
        for (const row of castRows.querySelectorAll('[data-slip-cast-id]')) {
            const input = row.querySelector('input');
            const amount = Number(input?.value);
            if (!input?.checkValidity() || !Number.isInteger(amount) || amount < 0 || amount > 999999999999) {
                input?.reportValidity();
                return null;
            }
            values.push({ slipCastId: Number(row.dataset.slipCastId), salesAmount: amount });
        }
        return values.length > 0 ? values : null;
    };

    const commandForDetail = (detail, adjustments) => ({
        operationId: createOperationId(),
        expectedBusinessDayId: overview.businessDayId,
        expectedBusinessDayRevision: overview.businessDayRevision,
        slipId: detail.slipId,
        expectedSlipVersion: detail.slipVersion,
        expectedCheckoutId: detail.checkoutId,
        expectedCheckoutVersion: detail.checkoutVersion,
        sourceAmountType: overview.sourceAmountType,
        splitMode: overview.splitMode,
        adjustments
    });

    const commandForAll = () => ({
        operationId: createOperationId(),
        expectedBusinessDayId: overview.businessDayId,
        expectedBusinessDayRevision: overview.businessDayRevision,
        slips: overview.details.map((detail) => ({
            slipId: detail.slipId,
            expectedSlipVersion: detail.slipVersion,
            expectedCheckoutId: detail.checkoutId,
            expectedCheckoutVersion: detail.checkoutVersion,
            sourceAmountType: overview.sourceAmountType,
            splitMode: overview.splitMode,
            adjustments: initialValues(detail).values.map((salesAmount, index) => ({
                slipCastId: detail.casts[index].slipCastId,
                salesAmount
            }))
        }))
    });

    const postCommand = async (url, body) => {
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': antiForgeryToken
            },
            body: JSON.stringify(body)
        });
        let payload = null;
        try {
            payload = await response.json();
        } catch {
            // The HTTP status still determines whether retrying the same operation is safe.
        }
        return { response, payload };
    };

    const applyConfirmed = (payload, kind) => {
        overview.businessDayRevision = payload.businessDayRevision;
        if (payload.overviewStatus) {
            overview.status = payload.overviewStatus;
        }

        if (kind === 'save' && payload.detail) {
            const detailIndex = overview.details.findIndex((detail) => Number(detail.slipId) === Number(payload.detail.slipId));
            if (detailIndex >= 0) {
                overview.details.splice(detailIndex, 1, payload.detail);
            }
        } else {
            const savedByCast = new Map((payload.savedAdjustments ?? []).map((row) => [Number(row.slipCastId), row]));
            overview.details.forEach((detail) => {
                detail.casts.forEach((cast) => {
                    const saved = savedByCast.get(Number(cast.slipCastId));
                    if (saved) {
                        cast.salesAmount = saved.salesAmount;
                        cast.sourceAmountType = saved.sourceAmountType;
                        cast.splitMode = saved.splitMode;
                    }
                });
            });
        }

        const savedBySlip = new Map();
        for (const row of payload.savedAdjustments ?? []) {
            const slipId = Number(row.slipId);
            const current = savedBySlip.get(slipId) ?? { count: 0, total: 0 };
            current.count += 1;
            current.total += Number(row.salesAmount) || 0;
            savedBySlip.set(slipId, current);
        }
        overview.slips.forEach((slip) => {
            const saved = savedBySlip.get(Number(slip.slipId));
            if (saved) {
                slip.savedCastCount = saved.count;
                slip.adjustedSalesAmountTotal = saved.total;
                slip.isAdjusted = saved.count >= Number(slip.requiredCastCount);
            }
        });
        overview.checkedAt = payload.dashboardDelta?.checkedAt ?? new Date().toISOString();
        if (payload.dashboardDelta) {
            window.dispatchEvent(new CustomEvent('prosper:closing-dashboard-delta', {
                detail: payload.dashboardDelta
            }));
        }
    };

    const executeMutation = async (kind, command) => {
        const url = kind === 'save' ? saveUrl : confirmUrl;
        pendingMutation = { kind, command };
        persistPendingMutation();
        clearMessages();
        confirmButton.disabled = true;
        if (kind === 'save') {
            setEditorDisabled(true);
        }
        setStatus(kind === 'save' ? '売上額調整を保存しています。' : '全件を確認しています。', 'saving');

        try {
            const { response, payload } = await postCommand(url, command);
            const classification = saveResponse?.classify({ response, payload }) ?? {
                confirmed: payload?.status === 'confirmed',
                rejected: ['conflict', 'validation_error', 'permission_denied', 'stale_work_item'].includes(payload?.status),
                retry: !payload || response.status >= 500 || payload?.status === 'unavailable'
            };
            if (classification.confirmed) {
                applyConfirmed(payload, kind);
                clearPendingMutation();
                if (kind === 'save') {
                    modal?.hide();
                }
                showSuccess(kind === 'save' ? 'キャスト売上額調整を保存しました。' : 'キャスト売上額調整を全件確認しました。');
                render();
                return;
            }

            if (classification.rejected) {
                clearPendingMutation();
                if (payload?.latestOverview) {
                    overview = payload.latestOverview;
                    activeDetail = null;
                    modal?.hide();
                    render();
                } else if (kind === 'save') {
                    setEditorDisabled(false);
                }
                showError(payload?.message ?? (payload?.status === 'conflict'
                    ? '他の端末で会計または調整内容が更新されました。最新の内容を確認してください。'
                    : '入力内容を確認してください。'));
                setStatus(payload?.status === 'conflict'
                    ? '競合を検出したため保存していません。'
                    : 'DB側で保存が拒否されました。', 'error');
                return;
            }

            throw new Error(payload?.message ?? `HTTP ${response.status}`);
        } catch (requestError) {
            if (kind === 'save') {
                saveButton.disabled = false;
                saveButton.textContent = '同じ操作を再送';
            } else {
                confirmButton.disabled = false;
                confirmButton.textContent = '同じ操作を再送';
            }
            showError('保存結果を確認できませんでした。同じ操作IDで再送してください。');
            setStatus(`通信結果が不明です: ${requestError.message}`, 'error');
        }
    };

    const loadOverview = async () => {
        if (loadPromise) {
            return loadPromise;
        }

        loadPromise = (async () => {
            clearMessages();
            loading.hidden = false;
            empty.hidden = true;
            list.hidden = true;
            actions.hidden = true;
            setStatus('状態を取得しています。', 'saving');
            try {
                const response = await fetch(overviewUrl, { credentials: 'same-origin' });
                const payload = await response.json();
                if (!response.ok || !payload?.succeeded || !payload.overview) {
                    throw new Error(payload?.message ?? `HTTP ${response.status}`);
                }
                overview = payload.overview;
                render();
                if (pendingMutation) {
                    void executeMutation(pendingMutation.kind, pendingMutation.command);
                }
            } catch (requestError) {
                loading.hidden = true;
                showError(`キャスト売上額調整を取得できませんでした。${requestError.message}`, true);
                setStatus('取得に失敗しました。', 'error');
            } finally {
                loadPromise = null;
            }
        })();
        return loadPromise;
    };

    form?.addEventListener('submit', (event) => {
        event.preventDefault();
        if (pendingMutation?.kind === 'save') {
            void executeMutation('save', pendingMutation.command);
            return;
        }
        const adjustments = readAdjustments();
        if (!activeDetail || !adjustments) {
            return;
        }
        saveButton.textContent = '保存';
        void executeMutation('save', commandForDetail(activeDetail, adjustments));
    });

    confirmButton?.addEventListener('click', () => {
        if (pendingMutation?.kind === 'confirm') {
            void executeMutation('confirm', pendingMutation.command);
            return;
        }
        if (!overview?.hasBusinessDay || overview.details.length === 0 ||
            overview.details.length !== overview.slips.length) {
            showError('確認できない伝票があります。最新の内容を取得してください。');
            return;
        }
        confirmButton.textContent = '全件確認';
        void executeMutation('confirm', commandForAll());
    });

    retryButton?.addEventListener('click', () => void loadOverview());
    modalElement?.addEventListener('hide.bs.modal', (event) => {
        if (pendingMutation?.kind === 'save') {
            event.preventDefault();
        }
    });
    modalElement?.addEventListener('hidden.bs.modal', () => {
        if (!pendingMutation) {
            activeDetail = null;
            saveButton.textContent = '保存';
        }
    });

    void loadOverview();
})();
