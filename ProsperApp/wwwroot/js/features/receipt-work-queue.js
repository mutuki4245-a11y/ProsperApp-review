(() => {
    const config = window.prosperReceiptWorkQueue ?? {};
    const form = document.querySelector('[data-receipt-work-queue-form]');
    if (config.enabled !== true || !form || !config.readUrl || !config.advanceUrl) return;

    const statusElement = document.querySelector('[data-receipt-queue-status]');
    const errorElement = document.querySelector('[data-receipt-queue-error]');
    const positionElement = document.querySelector('[data-receipt-queue-position]');
    const fileNameElements = document.querySelectorAll('[data-receipt-queue-file-name], [data-receipt-queue-preview-file-name]');
    const previewElement = document.querySelector('[data-receipt-queue-preview]');
    const previewLink = document.querySelector('[data-receipt-queue-preview-link]');
    const failedSection = document.querySelector('[data-receipt-failed-section]');
    const failedCount = document.querySelector('[data-receipt-failed-count]');
    const failedList = document.querySelector('[data-receipt-failed-list]');
    const documentIdInput = form.querySelector('[name="Input.DocumentId"]');
    const driveFileIdInput = form.querySelector('[name="Input.DriveFileId"]');
    const businessDayIdInput = form.querySelector('[name="Input.BusinessDayId"]');
    const paymentDateInput = form.querySelector('[name="Input.PaymentDate"]');
    const amountInput = form.querySelector('[name="Input.Amount"]');
    const accountSubjectInput = form.querySelector('[name="Input.AccountSubject"]');
    const accountSubjectSelected = document.getElementById('accountSubjectSelected');
    const advanceCastInput = form.querySelector('[name="Input.AdvanceCastId"]');
    const descriptionInput = form.querySelector('[name="Input.Description"]');
    const groupCodeInput = form.querySelector('[name="Input.GroupCode"]');
    const castAdvanceField = document.querySelector('[data-cast-advance-field]');
    const token = form.querySelector('[name="__RequestVerificationToken"]')?.value || '';
    const dbName = 'ProsperApp.ReceiptWorkQueue';
    const stateKey = 'outbox-v2';
    let state = { pending: [], failed: [] };
    let candidates = [];
    let current = null;
    let businessDay = null;
    let advanceCasts = [];
    let resumeCursor = null;
    let pendingCount = 0;
    let processing = false;
    let loading = false;
    let retryTimer = null;
    let editingFailedIndex = null;

    const openDatabase = () => new Promise((resolve, reject) => {
        const request = indexedDB.open(dbName, 1);
        request.onupgradeneeded = () => request.result.createObjectStore('state');
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
    const readState = async () => {
        try {
            const db = await openDatabase();
            const value = await new Promise((resolve, reject) => {
                const request = db.transaction('state', 'readonly').objectStore('state').get(stateKey);
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
            db.close();
            return {
                pending: Array.isArray(value?.pending) ? value.pending : [],
                failed: Array.isArray(value?.failed) ? value.failed : []
            };
        } catch {
            return { pending: [], failed: [] };
        }
    };
    const writeState = async () => {
        const db = await openDatabase();
        await new Promise((resolve, reject) => {
            const transaction = db.transaction('state', 'readwrite');
            transaction.objectStore('state').put(state, stateKey);
            transaction.oncomplete = resolve;
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error);
        });
        db.close();
    };
    const createOperationId = () => crypto.randomUUID();
    const queuedIds = () => new Set([
        ...state.pending.map((command) => command.documentId),
        ...state.failed.map((command) => command.documentId)
    ]);
    const escapeHtml = (value) => String(value ?? '')
        .replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;').replaceAll("'", '&#39;');
    const setError = (message = '') => {
        if (!errorElement) return;
        errorElement.textContent = message;
        errorElement.classList.toggle('d-none', !message);
    };
    const renderStatus = () => {
        const messages = [];
        if (state.pending.length) messages.push(`同期中 ${state.pending.length}件`);
        if (state.failed.length) messages.push(`要確認 ${state.failed.length}件`);
        if (!state.pending.length && !state.failed.length && current) messages.push('同期済み');
        statusElement.textContent = messages.join(' / ');
        statusElement.classList.toggle('d-none', messages.length === 0);
    };
    const renderFailures = () => {
        failedSection?.classList.toggle('d-none', state.failed.length === 0);
        if (failedCount) failedCount.textContent = String(state.failed.length);
        if (!failedList) return;
        failedList.replaceChildren();
        state.failed.forEach((command, index) => {
            const row = document.createElement('div');
            row.className = 'list-group-item d-flex align-items-center justify-content-between gap-3';
            const body = document.createElement('div');
            body.innerHTML = `<strong>${escapeHtml(command.item?.fileName || command.documentId)}</strong><div class="small text-danger">${escapeHtml(command.message)}</div>`;
            const actions = document.createElement('div');
            actions.className = 'd-flex gap-2';
            const edit = document.createElement('button');
            edit.type = 'button'; edit.className = 'btn btn-outline-primary btn-sm'; edit.textContent = '再編集'; edit.dataset.failedEdit = String(index);
            const withdraw = document.createElement('button');
            withdraw.type = 'button'; withdraw.className = 'btn btn-outline-secondary btn-sm'; withdraw.textContent = '取り下げ'; withdraw.dataset.failedWithdraw = String(index);
            actions.append(edit, withdraw); row.append(body, actions); failedList.appendChild(row);
        });
    };
    const renderPreview = (item) => {
        if (!item?.previewUrl) {
            previewElement.innerHTML = '<div class="preview-empty">プレビューできるファイルがありません。</div>';
            previewLink?.classList.add('d-none');
            return;
        }
        const url = String(item.previewUrl);
        previewElement.innerHTML = `<iframe id="receiptPreview" src="${escapeHtml(`${url}#toolbar=0&navpanes=0&view=Fit&zoom=page-fit`)}" title="証憑プレビュー"></iframe>`;
        previewLink.href = url;
        previewLink.classList.remove('d-none');
    };
    const renderAdvanceCasts = () => {
        const selected = advanceCastInput?.value || '';
        if (!advanceCastInput) return;
        advanceCastInput.replaceChildren(new Option('当日出勤のキャストを選択', ''));
        advanceCasts.forEach((cast) => advanceCastInput.add(new Option(cast.name || '', String(cast.id))));
        advanceCastInput.value = selected;
    };
    const syncCastAdvance = () => {
        const enabled = accountSubjectInput?.value === '前渡金: キャスト';
        castAdvanceField?.classList.toggle('d-none', !enabled);
        if (advanceCastInput) {
            advanceCastInput.disabled = !enabled;
            if (!enabled) advanceCastInput.value = '';
        }
    };
    const fillForm = (item, values = null) => {
        if (documentIdInput) documentIdInput.value = item?.id || '';
        if (driveFileIdInput) driveFileIdInput.value = item?.driveFileId || '';
        if (businessDayIdInput) businessDayIdInput.value = businessDay?.businessDayId || '';
        if (paymentDateInput) paymentDateInput.value = values?.paymentDate || item?.paymentDate || businessDay?.businessDate || '';
        if (amountInput) amountInput.value = values?.amount ?? item?.amount ?? '';
        if (accountSubjectInput) accountSubjectInput.value = values?.accountSubject || '';
        if (accountSubjectSelected) accountSubjectSelected.textContent = values?.accountSubject || '科目を選択してください';
        if (advanceCastInput) advanceCastInput.value = values?.advanceCastId || '';
        if (descriptionInput) descriptionInput.value = values?.description || '';
        if (groupCodeInput) groupCodeInput.value = values?.groupCode || '';
        document.querySelectorAll('[data-account-value]').forEach((button) => {
            const selected = button.dataset.accountValue === accountSubjectInput?.value;
            button.classList.toggle('is-selected', selected);
            button.setAttribute('aria-pressed', selected ? 'true' : 'false');
        });
        syncCastAdvance();
    };
    const renderCurrent = (values = null) => {
        const item = current;
        positionElement.textContent = item ? `残り ${pendingCount}件` : (state.pending.length ? '同期中' : '未処理なし');
        fileNameElements.forEach((element) => { element.textContent = item?.fileName || ''; });
        renderPreview(item);
        renderAdvanceCasts();
        fillForm(item, values);
        form.querySelectorAll('button[type="submit"]').forEach((button) => { button.disabled = !item; });
    };
    const mergeQueuePayload = (payload) => {
        if (Object.hasOwn(payload, 'businessDay')) businessDay = payload.businessDay;
        if (Array.isArray(payload.advanceCasts)) advanceCasts = payload.advanceCasts;
        pendingCount = Number(payload.pendingCount) || 0;
        resumeCursor = payload.resumeCursor || null;
        const incoming = [payload.workItem, ...(Array.isArray(payload.buffer) ? payload.buffer : [])].filter(Boolean);
        const unavailable = queuedIds();
        const currentStillPending = current && incoming.some((item) => String(item.id) === String(current.id));
        candidates = incoming.filter((item) =>
            !unavailable.has(item.id) && String(item.id) !== String(current?.id || ''));
        if (editingFailedIndex == null && (!currentStillPending || unavailable.has(current.id))) current = candidates.shift() || null;
        document.dispatchEvent(new CustomEvent('prosper:closing-dashboard-delta', {
            detail: { pendingReceiptCount: pendingCount }
        }));
        renderCurrent();
    };
    const loadQueue = async (cursor = null) => {
        if (loading) return;
        loading = true;
        try {
            const url = new URL(config.readUrl, window.location.href);
            if (cursor) url.searchParams.set('resumeCursor', cursor);
            const response = await fetch(url, { headers: { Accept: 'application/json' } });
            const payload = await response.json().catch(() => null);
            if (!response.ok || payload?.succeeded !== true) throw new Error(payload?.message || '領収書作業キューを取得できませんでした。');
            setError();
            mergeQueuePayload(payload);
        } catch (error) {
            setError(error?.message || '領収書作業キューを取得できませんでした。');
        } finally {
            loading = false;
        }
    };
    const moveNext = () => {
        current = candidates.shift() || null;
        renderCurrent();
        if (!current && resumeCursor) void loadQueue(resumeCursor);
    };
    const queueCommand = async (action) => {
        const item = current;
        if (!item?.id || !item.workItemToken) return;
        const amount = Number(amountInput?.value);
        const command = {
            operationId: createOperationId(), action,
            workItemToken: item.workItemToken, documentId: item.id,
            paymentDate: action === 'save' ? paymentDateInput?.value || null : null,
            amount: action === 'save' && Number.isFinite(amount) ? amount : null,
            accountSubject: action === 'save' ? accountSubjectInput?.value || null : null,
            description: action === 'save' ? descriptionInput?.value || null : null,
            groupCode: action === 'save' ? groupCodeInput?.value || null : null,
            advanceCastId: action === 'save' && advanceCastInput?.value ? Number(advanceCastInput.value) : null,
            item
        };
        const previousState = {
            pending: [...state.pending],
            failed: [...state.failed]
        };
        const previousEditingFailedIndex = editingFailedIndex;
        if (editingFailedIndex != null) state.failed.splice(editingFailedIndex, 1);
        state.pending.push(command);
        try {
            await writeState();
        } catch {
            state = previousState;
            editingFailedIndex = previousEditingFailedIndex;
            setError('端末へ未同期データを保存できませんでした。次の証憑へは進んでいません。');
            return;
        }
        editingFailedIndex = null;
        setError();
        renderStatus(); renderFailures(); moveNext(); void pump();
    };
    const scheduleRetry = () => {
        if (retryTimer || !state.pending.length) return;
        retryTimer = window.setTimeout(() => { retryTimer = null; void pump(); }, 3000);
    };
    const pump = async () => {
        if (processing || !state.pending.length) return;
        processing = true;
        const command = state.pending[0];
        try {
            const { item, ...payload } = command;
            const response = await fetch(config.advanceUrl, {
                method: 'POST', credentials: 'same-origin',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) },
                body: JSON.stringify(payload)
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || result?.succeeded !== true) {
                if (window.ProsperSaveResponse?.isRejectedStatus(result?.status)) {
                    state.pending.shift(); state.failed.push({ ...command, message: result?.message || '保存内容を確認してください。' });
                    await writeState(); renderStatus(); renderFailures();
                    return;
                }
                throw new Error('unavailable');
            }
            mergeQueuePayload(result);
            if (result.status === 'confirmed') {
                state.pending.shift();
            } else if (window.ProsperSaveResponse?.isRejectedStatus(result.status)) {
                state.pending.shift(); state.failed.push({ ...command, message: result.message || '保存内容を確認してください。' });
            } else {
                throw new Error('unavailable');
            }
            await writeState(); renderStatus(); renderFailures();
        } catch {
            scheduleRetry();
        } finally {
            processing = false;
            if (state.pending.length && !retryTimer) void pump();
        }
    };

    form.addEventListener('submit', (event) => {
        event.preventDefault();
        const action = event.submitter?.dataset.receiptQueueAction || 'save';
        if (action === 'skip') { editingFailedIndex = null; moveNext(); return; }
        if (action === 'save' && !form.reportValidity()) return;
        if (action === 'exclude_scan_mistake' && !window.confirm('スキャンミスとしてDB上で対象外にします。実行しますか？')) return;
        if (action === 'save' || action === 'exclude_scan_mistake') void queueCommand(action);
    });
    failedList?.addEventListener('click', async (event) => {
        const edit = event.target.closest('[data-failed-edit]');
        const withdraw = event.target.closest('[data-failed-withdraw]');
        const index = Number(edit?.dataset.failedEdit ?? withdraw?.dataset.failedWithdraw);
        if (!Number.isInteger(index) || !state.failed[index]) return;
        if (edit) {
            editingFailedIndex = index;
            current = state.failed[index].item;
            renderCurrent(state.failed[index]);
            window.scrollTo({ top: 0, behavior: 'smooth' });
        } else {
            state.failed.splice(index, 1);
            await writeState(); renderStatus(); renderFailures();
        }
    });
    document.querySelectorAll('[data-account-value]').forEach((button) => {
        button.addEventListener('click', () => {
            accountSubjectInput.value = button.dataset.accountValue || '';
            accountSubjectSelected.textContent = accountSubjectInput.value || '科目を選択してください';
            document.querySelectorAll('[data-account-value]').forEach((candidate) => {
                const selected = candidate === button;
                candidate.classList.toggle('is-selected', selected);
                candidate.setAttribute('aria-pressed', selected ? 'true' : 'false');
            });
            window.bootstrap?.Modal.getInstance(document.getElementById('accountSubjectModal'))?.hide();
            syncCastAdvance();
        });
    });
    window.addEventListener('online', () => { void pump(); if (!current) void loadQueue(); });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') { void pump(); if (!current && !state.pending.length) void loadQueue(); }
    });

    void (async () => {
        state = await readState();
        renderStatus(); renderFailures();
        await loadQueue();
        void pump();
    })();
})();
