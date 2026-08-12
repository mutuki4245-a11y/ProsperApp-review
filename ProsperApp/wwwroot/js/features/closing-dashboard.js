(() => {
    const root = document.querySelector('[data-closing-panels]');
    if (!root) {
        return;
    }

    const refreshIntervalMs = 30000;
    const panelClasses = ['is-preparing', 'is-ready', 'has-warning', 'needs-action'];
    const finalForm = document.querySelector('[data-closing-final]');
    const closeSubmit = finalForm?.querySelector('[data-closing-close-submit]');
    const finalStatus = finalForm?.querySelector('[data-closing-final-status]');
    const override = finalForm?.querySelector('[data-closing-override]');
    const memo = finalForm?.querySelector('[data-closing-memo]');
    const closeOperationIdInput = finalForm?.querySelector('[name="CloseOperationId"]');
    const businessDayIdInput = finalForm?.querySelector('[name="BusinessDayId"]');
    const businessDayRevisionInput = finalForm?.querySelector('[name="BusinessDayRevision"]');
    const departmentId = Number(root.dataset.departmentId) || 0;
    const closeCommandStorageKey = `prosper:closing-command:${departmentId}:v2`;
    const modalTargets = {
        drinkDelivery: '#drinkDeliveryEditorModal',
        castSalesAdjustment: '#castSalesAdjustmentShellModal',
        drinkBack: '#drinkBackEditorModal'
    };
    const castMasterStore = window.ProsperSync && departmentId > 0
        ? window.ProsperSync.getStore(`closing-cast-master:${departmentId}:v1`)
        : null;
    const saveResponse = window.ProsperSaveResponse;
    let dashboard = null;
    let refreshInFlight = null;
    let memoDirty = Boolean(memo?.value?.trim());
    let closeInFlight = false;
    let pendingCloseCommand = null;

    try {
        const storedCommand = JSON.parse(sessionStorage.getItem(closeCommandStorageKey) || 'null');
        if (storedCommand?.operationId && storedCommand?.expectedBusinessDayId) {
            pendingCloseCommand = storedCommand;
        }
    } catch {
        sessionStorage.removeItem(closeCommandStorageKey);
    }

    const toYen = (value) => `${Math.trunc(Number(value) || 0).toLocaleString('ja-JP')}円`;
    const drinkDeliveryStorageKey = (value) => `prosper:drink-delivery-editor:${value}`;
    const drinkBackStorageKey = (value) => `prosper:drink-back-editor:${value}`;

    const setPanel = (name, stateClass, badgeText, message, actionText) => {
        const panel = root.querySelector(`[data-closing-panel="${name}"]`);
        if (!panel) {
            return;
        }

        panel.classList.remove(...panelClasses);
        panel.classList.add(stateClass);
        const badge = panel.querySelector('[data-closing-badge]');
        badge?.classList.remove(...panelClasses);
        badge?.classList.add(stateClass);
        if (badge) {
            badge.textContent = badgeText;
        }

        const messageElement = panel.querySelector('[data-closing-message]');
        if (messageElement) {
            messageElement.textContent = message;
        }

        const action = panel.querySelector('[data-closing-action]');
        if (action && actionText) {
            action.textContent = actionText;
        }
    };

    const setAllPanelErrors = () => {
        ['openSlip', 'drinkDelivery', 'attendance', 'castSalesAdjustment', 'drinkBack', 'receipts']
            .forEach((name) => setPanel(name, 'has-warning', '取得失敗', '状態を取得できませんでした。', '再確認'));
    };

    const persistDrinkEditor = (value) => {
        const departmentId = Number(value?.departmentId) || 0;
        if (departmentId <= 0) {
            return;
        }

        const fragment = {
            departmentId,
            hasBusinessDay: Boolean(value.hasBusinessDay),
            businessDayId: value.businessDayId == null ? null : Number(value.businessDayId),
            businessDayRevision: Number(value.businessDayRevision) || 0,
            businessDate: value.businessDate || null,
            drinkDeliveryAmount: Number(value.drinkDeliveryAmount) || 0,
            isEntered: Boolean(value.isDrinkDeliveryAmountEntered),
            checkedAt: value.checkedAt || new Date().toISOString()
        };

        try {
            sessionStorage.setItem(drinkDeliveryStorageKey(departmentId), JSON.stringify(fragment));
        } catch {
            // The direct-URL recovery read remains available when storage is unavailable.
        }
    };

    const persistDrinkBackState = (value) => {
        const targetDepartmentId = Number(value?.departmentId) || departmentId;
        const editor = value?.drinkBackEditor;
        if (targetDepartmentId <= 0 || !editor) {
            return;
        }

        if (Array.isArray(value.activeCasts) && castMasterStore && value.castMasterRevision) {
            castMasterStore.replace({
                departmentId: targetDepartmentId,
                casts: value.activeCasts
            }, value.castMasterRevision, { force: true });
        }

        try {
            sessionStorage.setItem(drinkBackStorageKey(targetDepartmentId), JSON.stringify(editor));
        } catch {
            // The direct-URL recovery read remains available when storage is unavailable.
        }
    };

    const renderFinal = () => {
        if (!finalForm || !closeSubmit || !finalStatus) {
            return;
        }

        const hasBusinessDay = Boolean(dashboard?.hasBusinessDay);
        if (pendingCloseCommand) {
            closeSubmit.disabled = closeInFlight;
            closeSubmit.textContent = closeInFlight ? '締め処理中' : '同じ内容で再送';
            memo.disabled = true;
            if (override) {
                override.disabled = true;
            }
            finalForm.classList.remove(...panelClasses);
            finalForm.classList.add('is-preparing');
            finalStatus.dataset.saveState = 'saving';
            finalStatus.textContent = closeInFlight
                ? '営業日を締めています。'
                : '結果を確認できなかったため、同じ内容で再送してください。';
            return;
        }

        const canOverride = Boolean(override?.checked);
        const canSubmit = hasBusinessDay && (Boolean(dashboard?.canClose) || canOverride);
        closeSubmit.disabled = !canSubmit;
        closeSubmit.textContent = canSubmit ? '営業日を締める' : hasBusinessDay ? '未完了の項目があります' : '営業中の営業日がありません';
        memo.disabled = !hasBusinessDay;
        if (override) {
            override.disabled = !hasBusinessDay;
        }
        if (memo) {
            memo.placeholder = hasBusinessDay ? '必要なら共有事項を入力' : '営業中の営業日がありません';
        }

        finalForm.classList.remove(...panelClasses);
        finalForm.classList.add(canSubmit ? 'is-ready' : hasBusinessDay ? 'has-warning' : 'is-preparing');
        finalStatus.dataset.saveState = canSubmit ? 'done' : hasBusinessDay ? 'error' : 'idle';
        if (!hasBusinessDay) {
            finalStatus.textContent = '営業中の営業日がありません。';
        } else if (dashboard.canClose) {
            finalStatus.textContent = 'すべての必須項目が完了しています。';
        } else if (canOverride) {
            finalStatus.textContent = '管理者設定で締め条件を無視します。';
        } else {
            finalStatus.textContent = '未完了の項目があります。';
        }
    };

    const renderDashboard = (value) => {
        const previousBusinessDayId = dashboard?.businessDayId ?? null;
        const nextBusinessDayId = value?.businessDayId ?? null;
        const businessDayChanged = dashboard !== null && previousBusinessDayId !== nextBusinessDayId;
        dashboard = value;
        const hasBusinessDay = Boolean(value.hasBusinessDay);
        if (businessDayIdInput) {
            businessDayIdInput.value = hasBusinessDay ? String(value.businessDayId) : '';
        }
        if (businessDayRevisionInput) {
            businessDayRevisionInput.value = hasBusinessDay ? String(value.businessDayRevision) : '';
        }
        if (memo && (!memoDirty || businessDayChanged)) {
            memo.value = hasBusinessDay ? value.memo || '' : '';
            memoDirty = false;
        }
        if (businessDayChanged && closeOperationIdInput && !pendingCloseCommand) {
            closeOperationIdInput.value = crypto.randomUUID();
        }

        persistDrinkEditor(value);
        persistDrinkBackState(value);

        if (!hasBusinessDay) {
            setPanel('openSlip', 'is-preparing', '未開始', '営業中の営業日がありません。', '営業中を開く');
            setPanel('drinkDelivery', 'has-warning', '要入力', '酒代保存時に営業日を自動作成できます。', '入力');
            setPanel('attendance', 'has-warning', '要入力', '勤怠保存時に営業日を自動作成できます。', '入力');
            setPanel('castSalesAdjustment', 'has-warning', '要入力', '会計済み伝票ができたら売上額を調整します。', '開く');
            setPanel('drinkBack', 'has-warning', '要入力', '勤怠保存後にドリンクバックを調整します。', '開く');
        } else {
            const openSlipCount = Number(value.openSlipCount) || 0;
            setPanel(
                'openSlip',
                openSlipCount > 0 ? 'has-warning' : 'is-ready',
                openSlipCount > 0 ? '会計待ち' : '会計済',
                openSlipCount > 0 ? `${openSlipCount}件の伝票が未会計です。` : '未会計伝票はありません。',
                '営業中を開く');

            const drinkEntered = Boolean(value.isDrinkDeliveryAmountEntered);
            setPanel(
                'drinkDelivery',
                drinkEntered ? 'is-ready' : 'has-warning',
                drinkEntered ? '入力済' : '要入力',
                drinkEntered ? `営業日ごとの納品額 ${toYen(value.drinkDeliveryAmount)}` : '納品額が未入力です。',
                '入力');

            const attendanceCount = Number(value.attendanceCount) || 0;
            const missingClockOutCount = Number(value.missingClockOutCount) || 0;
            if (missingClockOutCount > 0) {
                setPanel('attendance', 'has-warning', '要入力', `${missingClockOutCount}名の退勤時刻が未入力です。`, '確認/修正');
            } else if (attendanceCount > 0) {
                setPanel('attendance', 'is-ready', '確認済', `${attendanceCount}名の勤怠を入力済みです。`, '確認/修正');
            } else {
                setPanel('attendance', 'has-warning', '要入力', '出勤者が登録されていません。', '入力');
            }

            const requiredSlipCount = Number(value.castSalesRequiredSlipCount) || 0;
            const missingSlipCount = Number(value.castSalesMissingSlipCount) || 0;
            const completedSlipCount = Number(value.castSalesCompletedSlipCount) || 0;
            if (requiredSlipCount === 0) {
                setPanel('castSalesAdjustment', 'is-ready', '調整済', '調整が必要な会計済み伝票はありません。', '開く');
            } else if (missingSlipCount === 0) {
                setPanel('castSalesAdjustment', 'is-ready', '調整済', `${completedSlipCount}件の伝票を調整済みです。`, '開く');
            } else {
                setPanel('castSalesAdjustment', 'has-warning', '要入力', `${missingSlipCount}件の伝票で売上額調整が未完了です。`, '開く');
            }

            const requiredCastCount = Number(value.drinkBackRequiredCastCount) || 0;
            const missingCastCount = Number(value.drinkBackMissingCastCount) || 0;
            const completedCastCount = Number(value.drinkBackCompletedCastCount) || 0;
            if (requiredCastCount === 0) {
                setPanel(
                    'drinkBack',
                    attendanceCount > 0 ? 'is-ready' : 'has-warning',
                    attendanceCount > 0 ? '入力済' : '要入力',
                    attendanceCount > 0 ? '調整必須の出勤キャストはいません。' : '出勤キャストを登録してから調整します。',
                    '開く');
            } else if (missingCastCount === 0) {
                setPanel('drinkBack', 'is-ready', '入力済', `${completedCastCount}名分を保存済みです。合計 ${toYen(value.drinkBackTotalAmount)}`, '開く');
            } else {
                setPanel('drinkBack', 'has-warning', '要入力', `${missingCastCount}名分の調整額が未入力です。`, '開く');
            }
        }

        if (root.dataset.receiptsEnabled === 'true') {
            const pendingReceiptCount = Number(value.pendingReceiptCount) || 0;
            setPanel(
                'receipts',
                pendingReceiptCount > 0 ? 'needs-action' : 'is-ready',
                pendingReceiptCount > 0 ? '任意' : '入力済',
                pendingReceiptCount > 0 ? `未入力領収書が ${pendingReceiptCount} 件あります。締め条件の対象外です。` : '未入力領収書はありません。',
                '入力');
        }

        renderFinal();
    };

    const openClosingModal = (name) => {
        const selector = modalTargets[name];
        const element = selector ? document.querySelector(selector) : null;
        if (!element || !window.bootstrap?.Modal) {
            return false;
        }

        window.bootstrap.Modal.getOrCreateInstance(element).show();
        return true;
    };

    const loadDashboard = async () => {
        const url = new URL(root.dataset.dashboardUrl, window.location.origin);
        const knownCastMasterRevision = castMasterStore?.getRevision();
        if (knownCastMasterRevision) {
            url.searchParams.set('knownCastMasterRevision', knownCastMasterRevision);
        }
        const response = await fetch(`${url.pathname}${url.search}`, {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' }
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.succeeded !== true) {
            throw new Error(result?.errorMessage || '締め状況を取得できませんでした。');
        }

        renderDashboard(result);
    };

    const refresh = () => {
        if (refreshInFlight) {
            return refreshInFlight;
        }

        refreshInFlight = loadDashboard()
            .catch((error) => {
                setAllPanelErrors();
                if (closeSubmit) {
                    closeSubmit.disabled = true;
                    closeSubmit.textContent = '再取得してください';
                }
                if (finalStatus) {
                    finalStatus.dataset.saveState = 'error';
                    finalStatus.textContent = error.message;
                }
            })
            .finally(() => {
                refreshInFlight = null;
            });
        return refreshInFlight;
    };

    const persistCloseCommand = (command) => {
        pendingCloseCommand = command;
        sessionStorage.setItem(closeCommandStorageKey, JSON.stringify(command));
    };

    const clearCloseCommand = () => {
        pendingCloseCommand = null;
        sessionStorage.removeItem(closeCommandStorageKey);
        if (closeOperationIdInput) {
            closeOperationIdInput.value = crypto.randomUUID();
        }
    };

    const createCloseCommand = () => ({
        operationId: closeOperationIdInput?.value || crypto.randomUUID(),
        expectedBusinessDayId: dashboard?.businessDayId ?? null,
        expectedBusinessDayRevision: dashboard?.businessDayRevision ?? null,
        memo: memo?.value || null,
        ignoreClosingRequirements: Boolean(override?.checked)
    });

    const submitClose = async (command) => {
        if (!finalForm || closeInFlight) {
            return;
        }

        closeInFlight = true;
        let finalFeedback = null;
        persistCloseCommand(command);
        renderFinal();
        try {
            const token = finalForm.querySelector('[name="__RequestVerificationToken"]')?.value || '';
            const response = await fetch(finalForm.action, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/json',
                    ...(token ? { RequestVerificationToken: token } : {})
                },
                body: JSON.stringify(command)
            });
            const result = await response.json().catch(() => null);
            const classification = saveResponse?.classify({ response, payload: result }) ?? {
                confirmed: result?.status === 'confirmed',
                rejected: ['conflict', 'validation_error', 'permission_denied', 'stale_work_item'].includes(result?.status),
                retry: !result || response.status >= 500 || result?.status === 'unavailable'
            };
            if (!result || classification.retry) {
                throw new Error(result?.message || '締め結果を確認できませんでした。');
            }

            clearCloseCommand();
            if (result.dashboard) {
                renderDashboard(result.dashboard);
            }

            if (classification.confirmed) {
                memoDirty = false;
                finalFeedback = { state: 'done', message: result.message || '営業日を締めました。' };
                if (result.reportBusinessDayId) {
                    window.dispatchEvent(new CustomEvent('prosper:daily-report-business-day', {
                        detail: { businessDayId: result.reportBusinessDayId }
                    }));
                }
                return;
            }

            finalFeedback = { state: 'error', message: result.message || '営業日を締められませんでした。' };
        } catch (error) {
            if (finalStatus) {
                finalStatus.dataset.saveState = 'error';
                finalStatus.textContent = error instanceof Error
                    ? `${error.message} 同じ内容で再送できます。`
                    : '締め結果を確認できませんでした。同じ内容で再送できます。';
            }
        } finally {
            closeInFlight = false;
            renderFinal();
            if (finalFeedback && finalStatus) {
                finalStatus.dataset.saveState = finalFeedback.state;
                finalStatus.textContent = finalFeedback.message;
            }
        }
    };

    memo?.addEventListener('input', () => {
        memoDirty = true;
    });
    override?.addEventListener('change', renderFinal);
    document.querySelector('[data-closing-retry]')?.addEventListener('click', () => void refresh());
    Object.keys(modalTargets).forEach((name) => {
        root.querySelector(`[data-closing-panel="${name}"]`)?.addEventListener('click', (event) => {
            if (openClosingModal(name)) {
                event.preventDefault();
            }
        });
    });
    window.addEventListener('prosper:attendance-editor-saved', () => void refresh());
    finalForm?.addEventListener('submit', (event) => {
        event.preventDefault();
        const command = pendingCloseCommand || createCloseCommand();
        if (!pendingCloseCommand && command.ignoreClosingRequirements &&
            !window.confirm('締め条件を無視して営業日を締めます。未完了の手順が残っていても続行しますか？')) {
            return;
        }
        void submitClose(command);
    });
    window.addEventListener('focus', () => {
        if (document.visibilityState === 'visible') {
            void refresh();
        }
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            void refresh();
        }
    });
    window.addEventListener('prosper:closing-dashboard-delta', (event) => {
        const delta = event.detail;
        if (!dashboard || Number(delta?.businessDayId) !== Number(dashboard.businessDayId)) {
            return;
        }

        const nextDashboard = {
            ...dashboard,
            businessDayRevision: delta.businessDayRevision,
            checkedAt: delta.checkedAt
        };
        if (delta.drinkDeliveryAmount !== undefined) {
            nextDashboard.drinkDeliveryAmount = delta.drinkDeliveryAmount;
            nextDashboard.isDrinkDeliveryAmountEntered = delta.isDrinkDeliveryAmountEntered;
        }
        const drinkBackEditor = delta.drinkBackEditor || delta.editor;
        if (drinkBackEditor) {
            nextDashboard.drinkBackRequiredCastCount = delta.drinkBackRequiredCastCount;
            nextDashboard.drinkBackCompletedCastCount = delta.drinkBackCompletedCastCount;
            nextDashboard.drinkBackMissingCastCount = delta.drinkBackMissingCastCount;
            nextDashboard.drinkBackTotalAmount = delta.drinkBackTotalAmount;
            nextDashboard.drinkBackEditor = drinkBackEditor;
            nextDashboard.canClose = delta.canClose;
            nextDashboard.blockReasons = delta.blockReasons;
        }
        renderDashboard(nextDashboard);
    });
    window.setInterval(() => {
        if (document.visibilityState === 'visible') {
            void refresh();
        }
    }, refreshIntervalMs);

    renderFinal();
    void refresh().then(() => {
        if (pendingCloseCommand) {
            void submitClose(pendingCloseCommand);
        }
    });
})();
