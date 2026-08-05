(() => {
    const config = window.ProsperManagementMasterConfig ?? {};
    if (!window.ProsperSync || !config.snapshotUrl || !config.departmentId) {
        return;
    }

    const store = window.ProsperSync.getStore(`management-master:${config.departmentId}:v1`);
    const buildUrl = (knownRevision) => {
        const url = new URL(config.snapshotUrl, window.location.origin);
        if (knownRevision) {
            url.searchParams.set('knownRevision', knownRevision);
        }
        return `${url.pathname}${url.search}`;
    };

    const refresh = () => store.hydrate('management-master', async (knownRevision) => {
        const response = await window.ProsperSync.requestJson(buildUrl(knownRevision));
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

    window.ProsperManagementMaster = Object.freeze({
        store,
        refresh
    });

    if (!store.get()) {
        refresh().catch(() => {
            // Individual management pages retain their current error rendering during migration.
        });
    }
})();
