(() => {
    const storagePrefix = 'prosper:sync-store:v1:';
    const stores = new Map();

    const clone = (value) => value === undefined ? undefined : JSON.parse(JSON.stringify(value));

    const readStorage = (key) => {
        try {
            const raw = window.localStorage.getItem(key);
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    };

    const writeStorage = (key, value) => {
        try {
            window.localStorage.setItem(key, JSON.stringify(value));
        } catch {
            // Storage is an optimization. Keep the in-memory state when it is unavailable.
        }
    };

    const deleteStorage = (key) => {
        try {
            window.localStorage.removeItem(key);
        } catch {
            // Nothing to do when storage is unavailable.
        }
    };

    const normalizeRevision = (value) => value === null || value === undefined ? null : String(value);

    const defaultIsNewer = (current, incoming) => {
        if (incoming === null) return false;
        if (current === null) return true;

        const currentNumber = Number(current);
        const incomingNumber = Number(incoming);
        if (Number.isFinite(currentNumber) && Number.isFinite(incomingNumber)) {
            return incomingNumber >= currentNumber;
        }

        return incoming !== current;
    };

    class SyncStore {
        constructor(name, options = {}) {
            this.name = String(name || '').trim();
            this.persist = options.persist !== false;
            this.storageKey = `${storagePrefix}${this.name}`;
            this.isNewer = options.isNewer || defaultIsNewer;
            this.listeners = new Set();
            this.inflight = new Map();
            this.state = this.persist ? readStorage(this.storageKey) : null;
        }

        get() {
            return clone(this.state);
        }

        getRevision() {
            return normalizeRevision(this.state?.revision);
        }

        subscribe(listener) {
            if (typeof listener !== 'function') {
                return () => {};
            }

            this.listeners.add(listener);
            return () => this.listeners.delete(listener);
        }

        replace(payload, revision, options = {}) {
            const incomingRevision = normalizeRevision(revision);
            const currentRevision = this.getRevision();
            const force = options.force === true;
            if (!force && !this.isNewer(currentRevision, incomingRevision)) {
                return false;
            }

            this.state = {
                revision: incomingRevision,
                payload: clone(payload),
                updatedAt: new Date().toISOString()
            };
            if (this.persist) {
                writeStorage(this.storageKey, this.state);
            }
            this.notify();
            return true;
        }

        patch(patch, revision) {
            const current = this.state?.payload;
            if (!current || typeof current !== 'object' || Array.isArray(current)) {
                return this.replace(patch, revision);
            }

            return this.replace({ ...current, ...clone(patch) }, revision);
        }

        clear() {
            this.state = null;
            this.inflight.clear();
            if (this.persist) {
                deleteStorage(this.storageKey);
            }
            this.notify();
        }

        async hydrate(key, loader) {
            const requestKey = String(key || 'default');
            if (this.inflight.has(requestKey)) {
                return this.inflight.get(requestKey);
            }

            const request = Promise.resolve()
                .then(() => loader(this.getRevision()))
                .then((response) => {
                    if (!response || response.unchanged === true) {
                        return this.get();
                    }

                    this.replace(response.payload, response.revision, {
                        force: response.force === true
                    });
                    return this.get();
                })
                .finally(() => this.inflight.delete(requestKey));

            this.inflight.set(requestKey, request);
            return request;
        }

        notify() {
            const snapshot = this.get();
            this.listeners.forEach((listener) => listener(snapshot));
        }
    }

    const getStore = (name, options) => {
        const normalizedName = String(name || '').trim();
        if (!normalizedName) {
            throw new Error('SyncStore name is required.');
        }

        if (!stores.has(normalizedName)) {
            stores.set(normalizedName, new SyncStore(normalizedName, options));
        }
        return stores.get(normalizedName);
    };

    const requestJson = async (url, options = {}) => {
        const optionHeaders = options.headers || {};
        const requestOptions = { ...options };
        delete requestOptions.headers;
        const response = await window.fetch(url, {
            credentials: 'same-origin',
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...optionHeaders
            },
            ...requestOptions
        });

        const payload = await response.json().catch(() => null);
        if (!response.ok) {
            const error = new Error(payload?.message || '同期に失敗しました。');
            error.status = response.status;
            error.payload = payload;
            throw error;
        }

        return payload;
    };

    window.ProsperSync = Object.freeze({
        getStore,
        requestJson
    });
})();
