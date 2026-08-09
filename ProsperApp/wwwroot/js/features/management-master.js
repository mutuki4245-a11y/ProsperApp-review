(() => {
    const config = window.ProsperManagementMasterConfig ?? {};
    if (!window.ProsperSync || !config.snapshotUrl || !config.mutationUrl || !config.departmentId) {
        return;
    }

    const store = window.ProsperSync.getStore(`management-master:${config.departmentId}:v1`);
    const saveResponse = window.ProsperSaveResponse;
    const areaPayloadKeys = Object.freeze({
        tables: 'tables',
        casts: 'casts',
        staffs: 'staffs',
        items: 'itemCatalog',
        nominations: 'nominationBacks',
        pricing: 'pricingPlan'
    });
    let lastRefreshAt = 0;
    const pendingMutationKey = `prosper:management-master-mutation:v1:${config.departmentId}`;
    const readPendingMutation = () => {
        try {
            const value = JSON.parse(sessionStorage.getItem(pendingMutationKey) || 'null');
            return value?.operationId ? value : null;
        } catch {
            return null;
        }
    };
    const writePendingMutation = (value) => {
        try {
            if (value) sessionStorage.setItem(pendingMutationKey, JSON.stringify(value));
            else sessionStorage.removeItem(pendingMutationKey);
        } catch {
            // The in-memory command still preserves the operation ID for this page lifetime.
        }
    };
    let pendingMutation = readPendingMutation();
    const buildUrl = (knownRevision) => {
        const url = new URL(config.snapshotUrl, window.location.origin);
        if (knownRevision) {
            url.searchParams.set('knownRevision', knownRevision);
        }
        return `${url.pathname}${url.search}`;
    };

    const refresh = () => store.hydrate('management-master', async (knownRevision) => {
        const response = await window.ProsperSync.requestJson(buildUrl(knownRevision));
        lastRefreshAt = Date.now();
        if (response.unchanged) {
            return { unchanged: true };
        }

        return {
            revision: response.revision,
            payload: {
                departmentId: response.departmentId,
                ...response.payload
            }
        };
    });

    const getVerificationToken = () =>
        document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    const mutate = async (area, action, payload) => {
        const state = store.get();
        const expectedAreaRevision = state?.payload?.revisions?.[area];
        if (!expectedAreaRevision || !areaPayloadKeys[area]) {
            throw new Error('管理マスタを同期してから保存してください。');
        }

        const contentKey = JSON.stringify({ area, action, payload });
        if (pendingMutation && pendingMutation.contentKey !== contentKey) {
            throw new Error('直前の保存結果を確認できていません。同じ内容でもう一度保存してください。');
        }
        if (!pendingMutation) {
            pendingMutation = {
                operationId: crypto.randomUUID(),
                area,
                action,
                expectedAreaRevision,
                payload,
                contentKey
            };
            writePendingMutation(pendingMutation);
        }
        const headers = {};
        const token = getVerificationToken();
        if (token) {
            headers.RequestVerificationToken = token;
        }

        let response;
        try {
            response = await window.ProsperSync.requestJson(config.mutationUrl, {
                method: 'POST',
                headers,
                body: JSON.stringify({
                    operationId: pendingMutation.operationId,
                    area: pendingMutation.area,
                    action: pendingMutation.action,
                    expectedAreaRevision: pendingMutation.expectedAreaRevision,
                    payload: pendingMutation.payload
                })
            });
        } catch (error) {
            const classification = saveResponse?.classify({
                response: { ok: false, status: Number(error?.status) || 0 },
                payload: error?.payload
            }) ?? {
                rejected: Number(error?.status) > 0 && Number(error.status) < 500,
                retry: !error?.payload || Number(error?.status) >= 500
            };
            if (classification.rejected) {
                pendingMutation = null;
                writePendingMutation(null);
            }
            throw error;
        }
        pendingMutation = null;
        writePendingMutation(null);

        const current = store.get();
        if (response.status === 'confirmed' &&
            current?.payload?.revisions?.[area] === expectedAreaRevision &&
            response.delta) {
            const nextPayload = {
                ...current.payload,
                [areaPayloadKeys[area]]: response.delta,
                revisions: {
                    ...current.payload.revisions,
                    [area]: response.areaRevision
                }
            };
            store.replace(nextPayload, response.revision, { force: true });
        }

        if (response.status === 'confirmed') {
            window.dispatchEvent(new CustomEvent('prosper:management-master-changed', {
                detail: { area, revision: response.areaRevision }
            }));
        }
        return response;
    };

    window.ProsperManagementMaster = Object.freeze({
        store,
        refresh,
        mutate
    });

    if (!store.get()) {
        refresh().catch(() => {
            // Individual management pages retain their current error rendering during migration.
        });
    }

    window.addEventListener('focus', () => {
        if (Date.now() - lastRefreshAt >= 60_000) {
            refresh().catch(() => {});
        }
    });
})();
