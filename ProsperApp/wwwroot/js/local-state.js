(() => {
    const appStoragePrefix = 'prosper:';

    const clearAppStorage = (storage) => {
        try {
            const keys = [];
            for (let index = 0; index < storage.length; index += 1) {
                const key = storage.key(index);
                if (key?.startsWith(appStoragePrefix)) {
                    keys.push(key);
                }
            }

            keys.forEach((key) => storage.removeItem(key));
            return { count: keys.length, cleared: true };
        } catch {
            return { count: 0, cleared: false };
        }
    };

    const clearCacheAndPendingQueues = () => {
        const local = clearAppStorage(window.localStorage);
        const session = clearAppStorage(window.sessionStorage);

        return {
            count: local.count + session.count,
            cleared: local.cleared && session.cleared
        };
    };

    window.ProsperLocalState = Object.freeze({
        clearCacheAndPendingQueues
    });

    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.matches('[data-clear-app-cache]')) {
            return;
        }

        const result = clearCacheAndPendingQueues();
        const field = form.querySelector('[data-client-state-cleared]');
        if (field) {
            field.value = String(result.cleared);
        }
    });
})();
