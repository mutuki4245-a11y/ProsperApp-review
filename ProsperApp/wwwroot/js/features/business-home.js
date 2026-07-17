(() => {
    const config = window.prosperBusinessHome ?? {};
    const form = document.querySelector('[data-business-karaoke-form]');
    const list = document.querySelector('[data-business-slip-list]');
    if (!form || !list) {
        return;
    }

    const saveStatus = window.TerminalSaveStatus;
    const status = form.querySelector('[data-business-karaoke-status]');
    const revealButton = document.querySelector('[data-slip-amount-reveal]');
    const businessDateDisplay = document.querySelector('[data-business-date-display]');
    const createDateInput = document.querySelector('[data-business-create-date-input]');
    const createDayIdInput = document.querySelector('[data-business-create-day-id-input]');
    const createDateDisplay = document.querySelector('[data-business-create-date-display]');
    const openSlipCount = document.querySelector('[data-business-open-slip-count]');
    const checkedOutSlipCount = document.querySelector('[data-business-checked-out-slip-count]');
    const businessSlipsUrl = config.businessSlipsUrl || '';
    const slipEditUrl = config.slipEditUrl || '';
    const draftKey = `prosper:business:${form.dataset.businessDayId || 'current'}:karaoke`;
    const refreshIntervalMs = 10000;
    const accountingUnit = 240;
    let slips = [];
    let hasLoaded = false;
    let refreshInFlight = false;
    let isSaving = false;
    let savePromise = null;
    let allowPageUnload = false;

    const formatYen = window.MoneyText.yen;
    const toQuantity = (value) => Math.max(0, Math.trunc(Number(value) || 0));
    const setText = (element, text) => {
        if (element && element.textContent !== String(text)) {
            element.textContent = String(text);
        }
    };

    const setKaraokeStatus = (state, message) => {
        saveStatus.set(status, state, message);
        if (status) {
            status.hidden = state === 'saved';
        }
    };

    const setAmountVisible = (visible) => {
        document.body.classList.toggle('slip-amounts-visible', visible);
        revealButton?.setAttribute('aria-pressed', visible ? 'true' : 'false');
    };

    const buildElement = (tagName, className, text) => {
        const element = document.createElement(tagName);
        if (className) {
            element.className = className;
        }
        if (text !== undefined && text !== null) {
            element.textContent = String(text);
        }
        return element;
    };
    const isValidBusinessDate = (value) => /^\d{4}-\d{2}-\d{2}$/.test(value || '') && value !== '0001-01-01';

    function loadDraft() {
        const raw = localStorage.getItem(draftKey);
        if (!raw) {
            return {};
        }

        try {
            const parsed = JSON.parse(raw);
            return parsed && typeof parsed === 'object' ? parsed : {};
        } catch {
            localStorage.removeItem(draftKey);
            return {};
        }
    }

    const getSlip = (slipId) => slips.find((slip) => String(slip.id) === String(slipId));
    const getInitialQuantity = (slip) => toQuantity(slip?.karaokeQuantity);
    const createKaraokeDraftState = () => {
        let draft = loadDraft();
        const write = () => {
            if (Object.keys(draft).length === 0) {
                localStorage.removeItem(draftKey);
            } else {
                localStorage.setItem(draftKey, JSON.stringify(draft));
            }
        };
        const cleanup = () => {
            let changed = false;
            Object.keys(draft).forEach((slipId) => {
                const slip = getSlip(slipId);
                if (!slip || slip.status !== 'open') {
                    delete draft[slipId];
                    changed = true;
                    return;
                }

                if (toQuantity(draft[slipId]) === getInitialQuantity(slip)) {
                    delete draft[slipId];
                    changed = true;
                }
            });

            if (changed) {
                write();
            }
        };

        return {
            getDisplayQuantity(slip) {
                const key = String(slip.id);
                return Object.prototype.hasOwnProperty.call(draft, key)
                    ? toQuantity(draft[key])
                    : getInitialQuantity(slip);
            },
            collectDirtyPayload() {
                cleanup();
                return Object.keys(draft)
                    .map((slipId) => {
                        const slip = getSlip(slipId);
                        if (!slip || slip.status !== 'open') {
                            return null;
                        }

                        return {
                            slip,
                            slipId,
                            quantity: toQuantity(draft[slipId]),
                            baseAmount: Number(slip.accountingAmount || 0) - getInitialQuantity(slip) * accountingUnit
                        };
                    })
                    .filter(Boolean);
            },
            cleanup,
            setQuantity(slip, quantity) {
                const normalized = toQuantity(quantity);
                if (normalized === getInitialQuantity(slip)) {
                    delete draft[String(slip.id)];
                } else {
                    draft[String(slip.id)] = normalized;
                }
                write();
            },
            markSaved(payloadRows) {
                payloadRows.forEach((line) => {
                    const slip = getSlip(line.slipId);
                    if (slip && toQuantity(draft[line.slipId]) === line.quantity) {
                        slip.karaokeQuantity = line.quantity;
                        slip.accountingAmount = line.baseAmount + line.quantity * accountingUnit;
                        delete draft[line.slipId];
                    }
                });
                write();
            },
            write
        };
    };
    const karaokeDraft = createKaraokeDraftState();
    const getDisplayQuantity = (slip) => karaokeDraft.getDisplayQuantity(slip);
    const cleanupDraft = () => karaokeDraft.cleanup();
    const collectDirtyPayload = () => karaokeDraft.collectDirtyPayload();

    const getRequestVerificationToken = () =>
        form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    const buildSavePayload = (payloadRows) => ({
        businessDayId: Number(form.dataset.businessDayId || 0) || null,
        karaokeLines: payloadRows.map((line) => ({
            slipId: Number(line.slipId),
            quantity: line.quantity
        }))
    });

    const buildSaveHeaders = () => {
        const headers = {
            'Accept': 'application/json',
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest'
        };
        const token = getRequestVerificationToken();
        if (token) {
            headers.RequestVerificationToken = token;
        }
        return headers;
    };

    const markDirtyStatus = () => {
        if (collectDirtyPayload().length === 0) {
            setKaraokeStatus('saved', '同期済み');
        } else {
            setKaraokeStatus('dirty');
        }
    };

    const allowNextPageUnload = () => {
        allowPageUnload = true;
        window.setTimeout(() => {
            allowPageUnload = false;
        }, 1500);
    };

    const shouldFlushForAnchor = (anchor, event) => {
        if (
            event.defaultPrevented ||
            event.button !== 0 ||
            event.altKey ||
            event.ctrlKey ||
            event.metaKey ||
            event.shiftKey ||
            anchor.hasAttribute('download')
        ) {
            return false;
        }

        const target = anchor.getAttribute('target');
        if (target && target.toLowerCase() !== '_self') {
            return false;
        }

        const rawHref = anchor.getAttribute('href')?.trim();
        if (!rawHref || rawHref === '#') {
            return false;
        }

        const lowerHref = rawHref.toLowerCase();
        if (
            lowerHref.startsWith('javascript:') ||
            lowerHref.startsWith('mailto:') ||
            lowerHref.startsWith('tel:')
        ) {
            return false;
        }

        const url = new URL(anchor.href, window.location.href);
        const isSamePageHash =
            url.origin === window.location.origin &&
            url.pathname === window.location.pathname &&
            url.search === window.location.search &&
            url.hash;

        return !isSamePageHash;
    };

    const updateSummary = (result) => {
        const businessDateText = result?.businessDateDisplay || '';
        const businessDateValue = result?.businessDate || '';
        const businessDayId = result?.businessDayId ? String(result.businessDayId) : '';
        if (businessDateDisplay && businessDateText && !businessDateText.startsWith('0001-01-01')) {
            businessDateDisplay.textContent = businessDateText;
        }
        if (createDateDisplay && isValidBusinessDate(businessDateValue)) {
            createDateDisplay.textContent = businessDateValue;
        }
        if (createDateInput && isValidBusinessDate(businessDateValue)) {
            createDateInput.value = businessDateValue;
        }
        if (createDayIdInput) {
            createDayIdInput.value = businessDayId;
        }
        if (businessDayId) {
            form.dataset.businessDayId = businessDayId;
        }

        if (openSlipCount) {
            openSlipCount.textContent = `${Number(result?.openSlipCount) || 0} 件`;
        }
        if (checkedOutSlipCount) {
            checkedOutSlipCount.textContent = `${Number(result?.checkedOutSlipCount) || 0} 件`;
        }
    };

    const renderEmpty = (title, message) => {
        Array.from(list.querySelectorAll('[data-business-slip-row]')).forEach((row) => row.remove());
        let row = list.querySelector('[data-business-empty-row]');
        if (!row) {
            list.innerHTML = '';
            row = buildElement('article', 'slip-list__row slip-list__row--add');
            row.dataset.businessEmptyRow = '';
            const main = buildElement('div', 'slip-list__main');
            const titleElement = buildElement('strong');
            titleElement.dataset.businessEmptyTitle = '';
            const messageElement = buildElement('span');
            messageElement.dataset.businessEmptyMessage = '';
            main.append(titleElement, messageElement);
            row.appendChild(main);

            list.appendChild(row);
        }

        setText(row.querySelector('[data-business-empty-title]'), title);
        setText(row.querySelector('[data-business-empty-message]'), message);
        if (revealButton) {
            revealButton.hidden = true;
        }
    };

    const buildAmountElement = (slip) => {
        const amount = buildElement('span', 'slip-list__amount slip-list__amount--concealed');
        amount.setAttribute('aria-label', '会計額');
        const amountValueElement = buildElement('strong', 'slip-list__amount-value', formatYen(slip.accountingAmount));
        amountValueElement.dataset.businessSlipAmountValue = '';
        amount.append(
            buildElement('span', 'slip-list__amount-mask', '**** 円'),
            amountValueElement
        );
        return amount;
    };

    const syncKaraokeControl = (row, slip) => {
        const amountValue = row.querySelector('[data-business-slip-amount-value]');
        let karaoke = row.querySelector('[data-business-karaoke-row]');
        if (slip.status !== 'open') {
            karaoke?.remove();
            setText(amountValue, formatYen(slip.accountingAmount));
            return;
        }

        const initialQuantity = getInitialQuantity(slip);
        const displayQuantity = getDisplayQuantity(slip);
        const baseAmount = Number(slip.accountingAmount || 0) - initialQuantity * accountingUnit;
        if (!karaoke) {
            karaoke = buildElement('div', 'slip-list__karaoke');
            karaoke.dataset.businessKaraokeRow = '';

            const decrement = buildElement('button', 'btn btn-outline-secondary', '-');
            decrement.type = 'button';
            decrement.dataset.businessKaraokeDecrement = '';
            const increment = buildElement('button', 'btn btn-outline-primary', '+');
            increment.type = 'button';
            increment.dataset.businessKaraokeIncrement = '';
            const quantity = buildElement('strong', null, displayQuantity);
            quantity.dataset.businessKaraokeDisplay = '';
            karaoke.append(buildElement('span', null, 'カラオケ'), decrement, quantity, increment);
            row.appendChild(karaoke);
        }

        karaoke.dataset.slipId = String(slip.id);
        karaoke.dataset.initialQuantity = String(initialQuantity);
        karaoke.dataset.quantity = String(displayQuantity);
        karaoke.dataset.accountingBaseAmount = String(baseAmount);
        karaoke.dataset.karaokeAccountingUnit = String(accountingUnit);
        karaoke.classList.toggle('is-dirty', displayQuantity !== initialQuantity);
        setText(karaoke.querySelector('[data-business-karaoke-display]'), displayQuantity);
        setText(amountValue, formatYen(baseAmount + displayQuantity * accountingUnit));
    };

    const syncSlipRow = (row, slip) => {
        row.dataset.slipId = String(slip.id);
        const link = row.querySelector('[data-business-slip-link]');
        if (link) {
            link.href = `${slipEditUrl}?slipId=${encodeURIComponent(slip.id)}`;
        }

        setText(row.querySelector('[data-business-slip-table]'), slip.tableDisplay);
        const statusElement = row.querySelector('[data-business-slip-status]');
        if (statusElement) {
            statusElement.className = `badge slip-list__status ${slip.statusBadgeClass}`;
            setText(statusElement, slip.statusDisplay);
        }
        setText(row.querySelector('[data-business-slip-time]'), slip.openedTime);
        setText(row.querySelector('[data-business-slip-customers]'), slip.customerNames || '客名なし');
        setText(row.querySelector('[data-business-slip-casts]'), slip.castNames || '指名なし');
        setText(row.querySelector('[data-business-slip-memo]'), slip.memo || '-');
        syncKaraokeControl(row, slip);
    };

    const buildSlipRow = (slip) => {
        const row = buildElement('article', 'slip-list__row slip-list__row--action');
        row.dataset.businessSlipRow = '';
        const link = buildElement('a', 'slip-list__row-main');
        link.dataset.businessSlipLink = '';

        const table = buildElement('strong', 'slip-list__table');
        table.dataset.businessSlipTable = '';
        const statusElement = buildElement('span');
        statusElement.dataset.businessSlipStatus = '';
        const openedTime = buildElement('span', 'slip-list__time');
        openedTime.dataset.businessSlipTime = '';
        const customers = buildElement('span', 'slip-list__customers');
        customers.dataset.businessSlipCustomers = '';
        const casts = buildElement('span', 'slip-list__casts');
        casts.dataset.businessSlipCasts = '';
        const memo = buildElement('span', 'slip-list__memo');
        memo.dataset.businessSlipMemo = '';

        link.append(table, statusElement, openedTime, customers, casts, memo);
        link.appendChild(buildAmountElement(slip));
        row.appendChild(link);

        syncSlipRow(row, slip);

        return row;
    };

    const renderSlips = () => {
        cleanupDraft();
        if (slips.length === 0) {
            renderEmpty('当日の伝票はまだありません', '最初の伝票作成時に営業日を自動作成します。');
            return;
        }

        Array.from(list.children).forEach((child) => {
            if (!child.matches('[data-business-slip-row]')) {
                child.remove();
            }
        });

        const rowsBySlipId = new Map(
            Array.from(list.querySelectorAll('[data-business-slip-row]'))
                .map((row) => [row.dataset.slipId, row])
        );
        const renderedSlipIds = new Set();
        slips.forEach((slip, index) => {
            const slipId = String(slip.id);
            const existingRow = rowsBySlipId.get(slipId);
            const row = existingRow ?? buildSlipRow(slip);
            if (existingRow) {
                syncSlipRow(row, slip);
            }

            renderedSlipIds.add(slipId);
            const current = list.children[index] ?? null;
            if (current !== row) {
                list.insertBefore(row, current);
            }
        });

        rowsBySlipId.forEach((row, slipId) => {
            if (!renderedSlipIds.has(slipId)) {
                row.remove();
            }
        });
        if (revealButton) {
            revealButton.hidden = false;
        }
    };

    const loadSlips = async () => {
        if (refreshInFlight) {
            return;
        }

        refreshInFlight = true;
        try {
            const response = await fetch(businessSlipsUrl, {
                headers: {
                    'Accept': 'application/json'
                }
            });
            if (!response.ok) {
                throw new Error('Business slips load failed.');
            }

            const result = await response.json();
            slips = Array.isArray(result.slips) ? result.slips : [];
            hasLoaded = true;
            updateSummary(result);
            renderSlips();
            if (!isSaving) {
                markDirtyStatus();
            }
        } catch {
            if (!hasLoaded) {
                renderEmpty('伝票を取得できませんでした', '次の自動更新で再取得します。');
            }
        } finally {
            refreshInFlight = false;
        }
    };

    const markSaved = (payloadRows) => {
        karaokeDraft.markSaved(payloadRows);
        renderSlips();
    };

    const submitDraftInternal = async () => {
        const payloadRows = collectDirtyPayload();
        if (payloadRows.length === 0) {
            karaokeDraft.write();
            setKaraokeStatus('saved', '同期済み');
            return true;
        }

        isSaving = true;
        setKaraokeStatus('saving');

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: JSON.stringify(buildSavePayload(payloadRows)),
                headers: buildSaveHeaders()
            });

            const result = await response.json().catch(() => null);
            if (!response.ok || (result && result.succeeded === false)) {
                throw new Error(result?.message || 'Karaoke save failed.');
            }

            markSaved(payloadRows);
            if (collectDirtyPayload().length === 0) {
                setKaraokeStatus('saved');
            } else {
                setKaraokeStatus('dirty');
            }
            return true;
        } catch {
            karaokeDraft.write();
            setKaraokeStatus('error');
            return false;
        } finally {
            isSaving = false;
        }
    };

    const submitDraft = () => {
        if (savePromise) {
            return savePromise;
        }

        savePromise = submitDraftInternal().finally(() => {
            savePromise = null;
        });
        return savePromise;
    };

    const navigateAfterFlush = async (link) => {
        window.AppLoading?.show();
        const saved = await submitDraft();
        if (!saved) {
            window.AppLoading?.hide();
            return;
        }

        allowNextPageUnload();
        window.location.href = link.href;
    };

    const submitAfterFlush = async (targetForm, submitter) => {
        window.AppLoading?.show(targetForm);
        const saved = await submitDraft();
        if (!saved) {
            window.AppLoading?.hide(targetForm);
            return;
        }

        window.AppLoading?.hide(targetForm);
        allowNextPageUnload();
        targetForm.dataset.karaokeFlushBypass = 'true';
        if (typeof targetForm.requestSubmit === 'function') {
            targetForm.requestSubmit(submitter || undefined);
        } else {
            targetForm.submit();
        }
    };

    form.addEventListener('click', (event) => {
        const decrement = event.target.closest('[data-business-karaoke-decrement]');
        const increment = event.target.closest('[data-business-karaoke-increment]');
        if (decrement || increment) {
            const row = event.target.closest('[data-business-karaoke-row]');
            if (!row) {
                return;
            }

            const slip = getSlip(row.dataset.slipId);
            if (!slip) {
                return;
            }

            const nextQuantity = toQuantity(row.dataset.quantity) + (increment ? 1 : -1);
            karaokeDraft.setQuantity(slip, nextQuantity);
            renderSlips();
            markDirtyStatus();
            return;
        }
    });

    document.addEventListener('click', (event) => {
        const link = event.target.closest('a[href]');
        if (!link || !shouldFlushForAnchor(link, event)) {
            return;
        }

        if (collectDirtyPayload().length === 0) {
            allowNextPageUnload();
            return;
        }

        if (link.origin === window.location.origin) {
            event.preventDefault();
            void navigateAfterFlush(link);
        }
    }, true);

    form.addEventListener('submit', (event) => {
        event.preventDefault();
        window.AppLoading?.show(form);
        void submitDraft().finally(() => window.AppLoading?.hide(form));
    });

    document.addEventListener('submit', (event) => {
        const targetForm = event.target;
        if (!(targetForm instanceof HTMLFormElement) || targetForm === form) {
            return;
        }

        if (targetForm.dataset.karaokeFlushBypass === 'true') {
            delete targetForm.dataset.karaokeFlushBypass;
            allowNextPageUnload();
            return;
        }

        if (event.defaultPrevented || !targetForm.checkValidity() || collectDirtyPayload().length === 0) {
            return;
        }

        event.preventDefault();
        void submitAfterFlush(targetForm, event.submitter);
    }, true);

    window.addEventListener('keydown', (event) => {
        if (event.key !== 'F5' && !(event.key.toLowerCase() === 'r' && (event.ctrlKey || event.metaKey))) {
            return;
        }

        event.preventDefault();
        markDirtyStatus();
    });

    window.addEventListener('beforeunload', (event) => {
        if (allowPageUnload || collectDirtyPayload().length === 0) {
            return;
        }

        event.preventDefault();
        event.returnValue = '';
    });

    revealButton?.addEventListener('pointerdown', () => setAmountVisible(true));
    document.addEventListener('pointerup', () => setAmountVisible(false));
    document.addEventListener('pointercancel', () => setAmountVisible(false));
    revealButton?.addEventListener('blur', () => setAmountVisible(false));
    revealButton?.addEventListener('keydown', (event) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            setAmountVisible(true);
        }
    });
    revealButton?.addEventListener('keyup', (event) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            setAmountVisible(false);
        }
    });

    window.addEventListener('online', () => {
        markDirtyStatus();
        void loadSlips();
    });
    window.addEventListener('focus', () => {
        if (document.visibilityState === 'visible') {
            void loadSlips();
        }
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            void loadSlips();
        }
    });
    window.setInterval(() => {
        if (document.visibilityState === 'visible') {
            void loadSlips();
        }
    }, refreshIntervalMs);

    void loadSlips();
})();
