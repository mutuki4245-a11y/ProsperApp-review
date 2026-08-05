(() => {
    const config = window.prosperReceiptWorkQueue ?? {};
    const form = document.querySelector('[data-receipt-work-queue-form]');
    const advanceUrl = config.advanceUrl || '';
    const items = Array.isArray(config.items) ? config.items : [];
    if (config.enabled !== true || !form || !advanceUrl || items.length === 0) {
        return;
    }

    const storageKey = 'ProsperApp.ReceiptWorkQueue.v1';
    const statusElement = document.querySelector('[data-receipt-queue-status]');
    const positionElement = document.querySelector('[data-receipt-queue-position]');
    const fileNameElements = document.querySelectorAll(
        '[data-receipt-queue-file-name], [data-receipt-queue-preview-file-name]');
    const previewElement = document.querySelector('[data-receipt-queue-preview]');
    const previewLink = document.querySelector('[data-receipt-queue-preview-link]');
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
    const requestVerificationToken = form.querySelector('[name="__RequestVerificationToken"]')?.value || '';
    let cursor = Math.max(0, Number(config.currentIndex) || 0);
    let processing = false;
    let retryTimer = null;

    const emptyState = () => ({ pending: [], failed: [] });

    const readState = () => {
        try {
            const parsed = JSON.parse(localStorage.getItem(storageKey) || '');
            return {
                pending: Array.isArray(parsed?.pending) ? parsed.pending : [],
                failed: Array.isArray(parsed?.failed) ? parsed.failed : []
            };
        } catch {
            return emptyState();
        }
    };

    let state = readState();

    const writeState = () => {
        try {
            localStorage.setItem(storageKey, JSON.stringify(state));
        } catch {
            // 保存領域が利用できない場合も、現在のタブでは直列送信を続ける。
        }
    };

    const queuedDocumentIds = () => new Set([
        ...state.pending.map((command) => command.documentId),
        ...state.failed.map((command) => command.documentId)
    ]);

    const currentItem = () => items[cursor] ?? null;

    const setStatus = () => {
        if (!statusElement) {
            return;
        }

        const messages = [];
        if (state.pending.length > 0) {
            messages.push(`同期中 ${state.pending.length}件`);
        }
        if (state.failed.length > 0) {
            messages.push(`要確認 ${state.failed.length}件`);
        }

        statusElement.textContent = messages.join(' / ');
        statusElement.classList.toggle('d-none', messages.length === 0);
    };

    const escapeHtml = (value) => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');

    const renderPreview = (item) => {
        if (!previewElement) {
            return;
        }

        if (!item?.previewUrl) {
            previewElement.innerHTML = '<div class="preview-empty">プレビューできるファイルがありません。</div>';
            previewLink?.classList.add('d-none');
            return;
        }

        const previewUrl = String(item.previewUrl);
        previewElement.innerHTML = `<iframe id="receiptPreview" src="${escapeHtml(`${previewUrl}#toolbar=0&navpanes=0&view=Fit&zoom=page-fit`)}" title="証憑プレビュー"></iframe>`;
        if (previewLink) {
            previewLink.href = previewUrl;
            previewLink.classList.remove('d-none');
        }
    };

    const syncCastAdvance = () => {
        const isCastAdvance = accountSubjectInput?.value === '前渡金: キャスト';
        castAdvanceField?.classList.toggle('d-none', !isCastAdvance);
        if (advanceCastInput) {
            advanceCastInput.disabled = !isCastAdvance;
            if (!isCastAdvance) {
                advanceCastInput.value = '';
            }
        }
    };

    const renderCurrent = () => {
        const item = currentItem();
        if (!item) {
            if (positionElement) {
                positionElement.textContent = state.pending.length > 0
                    ? '次の証憑を同期中です'
                    : '候補バッファを使い切りました';
            }
            fileNameElements.forEach((element) => {
                element.textContent = state.pending.length > 0 ? '保存内容を同期しています。' : '再読み込みしてください。';
            });
            renderPreview(null);
            form.querySelectorAll('button[type="submit"]').forEach((button) => {
                button.disabled = true;
            });
            return;
        }

        if (positionElement) {
            positionElement.textContent = `${cursor + 1} / ${config.totalCount || items.length}`;
        }
        fileNameElements.forEach((element) => {
            element.textContent = item.fileName || item.id;
        });
        renderPreview(item);
        form.querySelectorAll('button[type="submit"]').forEach((button) => {
            button.disabled = false;
        });

        if (documentIdInput) documentIdInput.value = item.id || '';
        if (driveFileIdInput) driveFileIdInput.value = item.driveFileId || '';
        if (businessDayIdInput) businessDayIdInput.value = config.businessDayId || '';
        if (paymentDateInput) paymentDateInput.value = item.paymentDate || '';
        if (amountInput) amountInput.value = item.amount ?? '';
        if (accountSubjectInput) accountSubjectInput.value = '';
        if (accountSubjectSelected) accountSubjectSelected.textContent = '科目を選択してください';
        if (advanceCastInput) advanceCastInput.value = '';
        if (descriptionInput) descriptionInput.value = '';
        if (groupCodeInput) groupCodeInput.value = '';
        document.querySelectorAll('[data-account-value]').forEach((button) => {
            button.classList.remove('is-selected');
        });
        syncCastAdvance();
    };

    const moveToNext = () => {
        const unavailable = queuedDocumentIds();
        let next = cursor + 1;
        while (next < items.length && unavailable.has(items[next]?.id)) {
            next += 1;
        }
        cursor = next;
        renderCurrent();
    };

    const createOperationId = () => {
        if (typeof crypto?.randomUUID === 'function') {
            return crypto.randomUUID();
        }

        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (character) => {
            const random = Math.floor(Math.random() * 16);
            const value = character === 'x' ? random : (random & 0x3) | 0x8;
            return value.toString(16);
        });
    };

    const queueCommand = (action) => {
        const item = currentItem();
        if (!item || !item.id || !item.workItemToken) {
            return;
        }

        const amount = Number(amountInput?.value);
        const command = {
            operationId: createOperationId(),
            action,
            workItemToken: item.workItemToken,
            documentId: item.id,
            paymentDate: paymentDateInput?.value || null,
            amount: Number.isFinite(amount) ? amount : null,
            accountSubject: accountSubjectInput?.value || null,
            description: descriptionInput?.value || null,
            groupCode: groupCodeInput?.value || null,
            advanceCastId: advanceCastInput?.value ? Number(advanceCastInput.value) : null
        };

        if (action === 'exclude_scan_mistake') {
            command.paymentDate = null;
            command.amount = null;
            command.accountSubject = null;
            command.description = null;
            command.groupCode = null;
            command.advanceCastId = null;
        }

        state.pending.push(command);
        writeState();
        setStatus();
        moveToNext();
        void pump();
    };

    const moveToFailed = (command, message) => {
        state.pending.shift();
        state.failed.push({ ...command, message: message || '保存内容を確認してください。' });
        writeState();
        setStatus();
    };

    const scheduleRetry = () => {
        if (retryTimer || state.pending.length === 0) {
            return;
        }
        retryTimer = window.setTimeout(() => {
            retryTimer = null;
            void pump();
        }, 3000);
    };

    const pump = async () => {
        if (processing || state.pending.length === 0) {
            return;
        }

        processing = true;
        const command = state.pending[0];
        try {
            const response = await fetch(advanceUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json',
                    ...(requestVerificationToken ? { RequestVerificationToken: requestVerificationToken } : {})
                },
                body: JSON.stringify(command)
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                if (response.status >= 400 && response.status < 500) {
                    moveToFailed(command, payload.message);
                    return;
                }
                throw new Error(payload.message || 'receipt_queue_unavailable');
            }

            if (payload.succeeded !== true) {
                throw new Error(payload.message || 'receipt_queue_unavailable');
            }

            if (payload.status === 'confirmed') {
                state.pending.shift();
                writeState();
                setStatus();
                return;
            }

            if (payload.status === 'validation_error' || payload.status === 'stale_work_item') {
                moveToFailed(command, payload.message);
                return;
            }

            throw new Error(payload.message || 'receipt_queue_unavailable');
        } catch {
            // 成否不明の通信失敗は同じ operation_id を保ったまま先頭で再送する。
            scheduleRetry();
        } finally {
            processing = false;
            if (state.pending.length > 0 && !retryTimer) {
                void pump();
            }
        }
    };

    form.addEventListener('submit', (event) => {
        const action = event.submitter?.dataset.receiptQueueAction || 'save';
        if (action === 'skip') {
            event.preventDefault();
            moveToNext();
            return;
        }

        if (action !== 'save' && action !== 'exclude_scan_mistake') {
            return;
        }

        if (action === 'save' && !form.reportValidity()) {
            return;
        }

        event.preventDefault();
        queueCommand(action);
    });

    window.addEventListener('online', () => void pump());
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            void pump();
        }
    });

    setStatus();
    renderCurrent();
    void pump();
})();
