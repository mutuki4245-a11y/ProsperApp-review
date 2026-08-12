(() => {
    const root = document.querySelector('[data-sales-history]');
    if (!root) {
        return;
    }

    const historyUrl = root.dataset.historyPageUrl;
    const editorUrl = root.dataset.correctionEditorUrl;
    const saveUrl = root.dataset.correctionSaveUrl;
    const filterForm = root.querySelector('[data-history-filter-form]');
    const fromDateInput = root.querySelector('[data-history-from-date]');
    const toDateInput = root.querySelector('[data-history-to-date]');
    const list = root.querySelector('[data-history-list]');
    const loading = root.querySelector('[data-history-loading]');
    const empty = root.querySelector('[data-history-empty]');
    const error = root.querySelector('[data-history-error]');
    const count = root.querySelector('[data-history-count]');
    const loadMoreButton = root.querySelector('[data-history-load-more]');
    const selectionEmpty = root.querySelector('[data-history-selection-empty]');
    const detail = root.querySelector('[data-history-detail]');
    const reportSection = root.querySelector('[data-history-report]');
    const reportUnavailable = root.querySelector('[data-history-report-unavailable]');
    const correctionButton = root.querySelector('[data-history-correction-open]');
    const closingLink = root.querySelector('[data-history-closing-link]');
    const token = root.querySelector('[data-sales-history-token-form] input[name="__RequestVerificationToken"]')?.value || '';
    const modalElement = document.getElementById('salesHistoryCorrectionModal');
    const correctionForm = modalElement?.querySelector('[data-sales-history-correction-form]');
    const modal = modalElement && window.bootstrap?.Modal
        ? window.bootstrap.Modal.getOrCreateInstance(modalElement)
        : null;

    const state = {
        items: [],
        hasMore: false,
        nextBeforeBusinessDate: null,
        nextBeforeBusinessDayId: null,
        selectedBusinessDayId: null,
        editor: null,
        operationId: null,
        requestSequence: 0,
        loading: false
    };

    const yenFormatter = new Intl.NumberFormat('ja-JP', {
        style: 'currency',
        currency: 'JPY',
        maximumFractionDigits: 0
    });
    const dateFormatter = new Intl.DateTimeFormat('ja-JP', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        weekday: 'short'
    });

    const pick = (source, camelName, snakeName = camelName) =>
        source?.[camelName] ?? source?.[snakeName];
    const asNumber = (value, fallback = 0) => {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    };
    const asNullableNumber = (value) => {
        if (value === null || value === undefined || value === '') {
            return null;
        }
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : null;
    };
    const formatYen = (value) => yenFormatter.format(asNumber(value));
    const formatDate = (value) => {
        if (!value) {
            return '-';
        }
        const parsed = new Date(`${value}T00:00:00+09:00`);
        return Number.isNaN(parsed.valueOf()) ? String(value) : dateFormatter.format(parsed);
    };
    const setText = (selector, value) => {
        const element = root.querySelector(selector);
        if (element) {
            element.textContent = value;
        }
    };
    const makeElement = (tagName, className = '', textContent = '') => {
        const element = document.createElement(tagName);
        if (className) {
            element.className = className;
        }
        if (textContent) {
            element.textContent = textContent;
        }
        return element;
    };
    const readErrorMessage = (payload, fallback) =>
        payload?.errorMessage || payload?.message || fallback;

    const requestJson = async (url, options = {}) => {
        const response = await fetch(url, {
            credentials: 'same-origin',
            cache: 'no-store',
            ...options,
            headers: {
                Accept: 'application/json',
                ...(options.headers ?? {})
            }
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok) {
            const requestError = new Error(readErrorMessage(payload, '処理を完了できませんでした。'));
            requestError.status = response.status;
            requestError.payload = payload;
            throw requestError;
        }
        return payload;
    };

    const normalizeSummary = (item) => ({
        businessDayId: asNumber(pick(item, 'businessDayId', 'business_day_id')),
        businessDate: pick(item, 'businessDate', 'business_date'),
        status: pick(item, 'status') || '',
        salesAmount: asNumber(pick(item, 'salesAmount', 'sales_amount')),
        customerCount: asNumber(pick(item, 'customerCount', 'customer_count')),
        slipCount: asNumber(pick(item, 'slipCount', 'slip_count')),
        memo: pick(item, 'memo') || '',
        latestSnapshotVersion: asNullableNumber(pick(item, 'latestSnapshotVersion', 'latest_snapshot_version')),
        reportAvailable: Boolean(pick(item, 'reportAvailable', 'report_available')),
        isCorrected: Boolean(pick(item, 'isCorrected', 'is_corrected')),
        canEdit: Boolean(pick(item, 'canEdit', 'can_edit')),
        editReason: pick(item, 'editReason', 'edit_reason') || ''
    });

    const normalizePage = (payload) => {
        const page = payload?.page ?? payload?.value ?? payload ?? {};
        const sourceItems = Array.isArray(page) ? page : (page.items ?? page.summaries ?? []);
        return {
            items: sourceItems.map(normalizeSummary).filter((item) => item.businessDayId > 0),
            hasMore: Boolean(pick(page, 'hasMore', 'has_more')),
            nextBeforeBusinessDate: pick(page, 'nextBeforeBusinessDate', 'next_before_business_date') || null,
            nextBeforeBusinessDayId: asNullableNumber(pick(page, 'nextBeforeBusinessDayId', 'next_before_business_day_id'))
        };
    };

    const setHistoryError = (message = '') => {
        error.textContent = message;
        error.hidden = !message;
    };

    const updateUrl = () => {
        const url = new URL(window.location.href);
        const assignments = {
            businessDayId: state.selectedBusinessDayId,
            fromBusinessDate: fromDateInput.value,
            toBusinessDate: toDateInput.value
        };
        Object.entries(assignments).forEach(([name, value]) => {
            if (value) {
                url.searchParams.set(name, String(value));
            } else {
                url.searchParams.delete(name);
            }
        });
        window.history.replaceState({}, '', `${url.pathname}${url.search}${url.hash}`);
    };

    const statusDefinition = (status) => status === 'open'
        ? { label: '営業中', badgeClass: 'text-bg-warning' }
        : { label: '締め済み', badgeClass: 'text-bg-success' };

    const createHistoryItem = (item) => {
        const button = makeElement('button', 'sales-history-item');
        button.type = 'button';
        button.dataset.businessDayId = String(item.businessDayId);
        button.setAttribute('aria-pressed', item.businessDayId === state.selectedBusinessDayId ? 'true' : 'false');
        button.classList.toggle('is-selected', item.businessDayId === state.selectedBusinessDayId);

        const heading = makeElement('span', 'sales-history-item__heading');
        heading.append(makeElement('strong', '', formatDate(item.businessDate)));
        const badges = makeElement('span', 'sales-history-item__badges');
        const definition = statusDefinition(item.status);
        badges.append(makeElement('span', `badge ${definition.badgeClass}`, definition.label));
        if (item.isCorrected) {
            badges.append(makeElement('span', 'badge text-bg-info', '補正済み'));
        }
        if (!item.reportAvailable) {
            badges.append(makeElement('span', 'badge text-bg-secondary', '日報なし'));
        }
        heading.append(badges);

        const summary = makeElement('span', 'sales-history-item__summary');
        summary.append(
            makeElement('strong', '', formatYen(item.salesAmount)),
            makeElement('span', '', `${item.customerCount}名`),
            makeElement('span', '', `${item.slipCount}組`)
        );
        button.append(heading, summary);
        return button;
    };

    const renderList = () => {
        list.replaceChildren(...state.items.map(createHistoryItem));
        empty.hidden = state.items.length > 0 || state.loading;
        loadMoreButton.hidden = !state.hasMore;
        loadMoreButton.disabled = state.loading;
        count.textContent = state.items.length === 0
            ? '0件'
            : `${state.items.length}件${state.hasMore ? '以上' : ''}`;
    };

    const renderDetail = () => {
        const item = state.items.find((candidate) => candidate.businessDayId === state.selectedBusinessDayId);
        selectionEmpty.hidden = Boolean(item);
        detail.hidden = !item;
        if (!item) {
            reportSection.hidden = true;
            reportUnavailable.hidden = true;
            return;
        }

        const definition = statusDefinition(item.status);
        const statusBadge = root.querySelector('[data-history-selected-status]');
        statusBadge.className = `badge ${definition.badgeClass}`;
        statusBadge.textContent = definition.label;
        root.querySelector('[data-history-selected-corrected]').hidden = !item.isCorrected;
        setText('[data-history-selected-date]', formatDate(item.businessDate));
        setText('[data-history-selected-memo]', item.memo.trim() || '締めメモなし');
        setText('[data-history-selected-sales]', formatYen(item.salesAmount));
        setText('[data-history-selected-customers]', `${item.customerCount}名`);
        setText('[data-history-selected-slips]', `${item.slipCount}組`);

        const isOpen = item.status === 'open';
        closingLink.hidden = !isOpen;
        correctionButton.hidden = isOpen || !item.canEdit;
        correctionButton.dataset.businessDayId = String(item.businessDayId);

        const warning = root.querySelector('[data-history-warning]');
        const warnings = [];
        if (item.editReason && !item.canEdit) {
            warnings.push(item.editReason);
        }
        if (!item.reportAvailable) {
            warnings.push('締め済みスナップショットがないため、保存済みの日報を閲覧できません。');
        }
        warning.textContent = [...new Set(warnings)].join(' ');
        warning.hidden = warnings.length === 0;

        reportSection.hidden = !item.reportAvailable;
        reportUnavailable.hidden = item.reportAvailable;
        if (item.reportAvailable) {
            window.requestAnimationFrame(() => {
                window.dispatchEvent(new CustomEvent('prosper:daily-report-business-day', {
                    detail: { businessDayId: item.businessDayId }
                }));
            });
        }
    };

    const selectBusinessDay = (businessDayId, updateBrowserUrl = true) => {
        const item = state.items.find((candidate) => candidate.businessDayId === businessDayId);
        if (!item) {
            return false;
        }
        state.selectedBusinessDayId = businessDayId;
        list.querySelectorAll('[data-business-day-id]').forEach((button) => {
            const selected = asNumber(button.dataset.businessDayId) === businessDayId;
            button.classList.toggle('is-selected', selected);
            button.setAttribute('aria-pressed', selected ? 'true' : 'false');
        });
        renderDetail();
        if (updateBrowserUrl) {
            updateUrl();
        }
        return true;
    };

    const buildHistoryPageUrl = (append) => {
        const url = new URL(historyUrl, window.location.href);
        if (fromDateInput.value) {
            url.searchParams.set('fromBusinessDate', fromDateInput.value);
        }
        if (toDateInput.value) {
            url.searchParams.set('toBusinessDate', toDateInput.value);
        }
        if (append && state.nextBeforeBusinessDate && state.nextBeforeBusinessDayId) {
            url.searchParams.set('beforeBusinessDate', state.nextBeforeBusinessDate);
            url.searchParams.set('beforeBusinessDayId', String(state.nextBeforeBusinessDayId));
        }
        return `${url.pathname}${url.search}`;
    };

    const loadHistoryPage = async ({ append = false, render = true } = {}) => {
        if (state.loading || (append && !state.hasMore)) {
            return false;
        }
        const sequence = append ? state.requestSequence : ++state.requestSequence;
        state.loading = true;
        loading.hidden = append;
        if (append) {
            loadMoreButton.disabled = true;
            loadMoreButton.textContent = '取得中';
        }
        try {
            const page = normalizePage(await requestJson(buildHistoryPageUrl(append)));
            if (sequence !== state.requestSequence) {
                return false;
            }
            const existingIds = new Set(append ? state.items.map((item) => item.businessDayId) : []);
            state.items = append
                ? [...state.items, ...page.items.filter((item) => !existingIds.has(item.businessDayId))]
                : page.items;
            state.hasMore = page.hasMore;
            state.nextBeforeBusinessDate = page.nextBeforeBusinessDate;
            state.nextBeforeBusinessDayId = page.nextBeforeBusinessDayId;
            setHistoryError();
            if (render) {
                renderList();
            }
            return true;
        } catch (requestError) {
            setHistoryError(requestError instanceof Error
                ? requestError.message
                : '営業履歴を取得できませんでした。');
            return false;
        } finally {
            state.loading = false;
            loading.hidden = true;
            loadMoreButton.disabled = false;
            loadMoreButton.textContent = 'さらに表示';
            if (render) {
                renderList();
            }
        }
    };

    const loadInitialHistory = async (preferredBusinessDayId = null) => {
        state.items = [];
        state.hasMore = true;
        state.nextBeforeBusinessDate = null;
        state.nextBeforeBusinessDayId = null;
        state.selectedBusinessDayId = null;
        renderDetail();
        const loaded = await loadHistoryPage();
        if (!loaded) {
            return;
        }

        while (preferredBusinessDayId &&
               !state.items.some((item) => item.businessDayId === preferredBusinessDayId) &&
               state.hasMore) {
            const appended = await loadHistoryPage({ append: true, render: false });
            if (!appended) {
                break;
            }
        }
        renderList();
        const selected = preferredBusinessDayId && selectBusinessDay(preferredBusinessDayId, false);
        if (!selected && state.items[0]) {
            selectBusinessDay(state.items[0].businessDayId);
        } else if (selected) {
            updateUrl();
        }
    };

    const normalizeAttendanceEntry = (entry, type) => ({
        type,
        id: asNumber(pick(entry, type === 'cast' ? 'castId' : 'staffId', type === 'cast' ? 'cast_id' : 'staff_id')),
        displayName: pick(entry, 'displayName', 'display_name') || '',
        departmentName: pick(entry, 'departmentName', 'department_name') || '',
        employmentType: pick(entry, 'employmentType', 'employment_type') || '',
        isActiveCandidate: Boolean(pick(entry, 'isActiveCandidate', 'is_active_candidate')),
        isSelected: Boolean(pick(entry, 'isSelected', 'is_selected')),
        clockInTime: pick(entry, 'clockInTime', 'clock_in_time') || '',
        clockOutTime: pick(entry, 'clockOutTime', 'clock_out_time') || '',
        usesSendService: Boolean(pick(entry, 'usesSendService', 'uses_send_service'))
    });

    const normalizeEditor = (payload) => {
        const editor = payload?.editor ?? payload?.value ?? payload ?? {};
        return {
            businessDayId: asNumber(pick(editor, 'businessDayId', 'business_day_id')),
            businessDate: pick(editor, 'businessDate', 'business_date'),
            snapshotVersion: asNullableNumber(pick(editor, 'snapshotVersion', 'snapshot_version')),
            canEdit: Boolean(pick(editor, 'canEdit', 'can_edit')),
            editReason: pick(editor, 'editReason', 'edit_reason') || '',
            isCorrected: Boolean(pick(editor, 'isCorrected', 'is_corrected')),
            memo: pick(editor, 'memo') || '',
            drinkDeliveryAmount: asNumber(pick(editor, 'drinkDeliveryAmount', 'drink_delivery_amount')),
            castEntries: (pick(editor, 'castEntries', 'cast_entries') ?? [])
                .map((entry) => normalizeAttendanceEntry(entry, 'cast')),
            staffEntries: (pick(editor, 'staffEntries', 'staff_entries') ?? [])
                .map((entry) => normalizeAttendanceEntry(entry, 'staff')),
            drinkBackAdjustments: (pick(editor, 'drinkBackAdjustments', 'drink_back_adjustments') ?? []).map((entry) => ({
                castId: asNumber(pick(entry, 'castId', 'cast_id')),
                displayName: pick(entry, 'displayName', 'display_name') || '',
                departmentName: pick(entry, 'departmentName', 'department_name') || '',
                isRequired: Boolean(pick(entry, 'isRequired', 'is_required')),
                isActive: Boolean(pick(entry, 'isActive', 'is_active')),
                adjustmentAmount: asNullableNumber(pick(entry, 'adjustmentAmount', 'adjustment_amount')),
                isActiveCandidate: Boolean(pick(entry, 'isActiveCandidate', 'is_active_candidate'))
            })),
            castSalesSlips: (pick(editor, 'castSalesSlips', 'cast_sales_slips') ?? []).map((slip) => ({
                slipId: asNumber(pick(slip, 'slipId', 'slip_id')),
                tableCode: pick(slip, 'tableCode', 'table_code') || '',
                tableName: pick(slip, 'tableName', 'table_name') || '',
                customerNames: pick(slip, 'customerNames', 'customer_names') || '',
                totalAmount: asNumber(pick(slip, 'totalAmount', 'total_amount')),
                sourceAmountType: pick(slip, 'sourceAmountType', 'source_amount_type') || 'total',
                splitMode: pick(slip, 'splitMode', 'split_mode') || 'split',
                adjustments: (pick(slip, 'adjustments') ?? []).map((adjustment) => ({
                    slipCastId: asNumber(pick(adjustment, 'slipCastId', 'slip_cast_id')),
                    castId: asNumber(pick(adjustment, 'castId', 'cast_id')),
                    displayName: pick(adjustment, 'displayName', 'cast_display_name') || '',
                    salesAmount: asNullableNumber(pick(adjustment, 'salesAmount', 'sales_amount')),
                    suggestedSalesAmount: asNullableNumber(pick(adjustment, 'suggestedSalesAmount', 'suggested_sales_amount'))
                }))
            }))
        };
    };

    const updateCorrectionStatus = (message, saveState = 'idle') => {
        const status = modalElement.querySelector('[data-correction-status]');
        status.textContent = message;
        status.dataset.saveState = saveState;
    };

    const setCorrectionError = (message = '') => {
        const correctionError = modalElement.querySelector('[data-correction-error]');
        correctionError.textContent = message;
        correctionError.hidden = !message;
    };

    const markCorrectionDirty = () => updateCorrectionStatus('未保存の変更があります。', 'dirty');

    const createAttendanceRow = (entry) => {
        const row = makeElement('div', 'sales-history-attendance-row');
        row.dataset.attendanceType = entry.type;
        row.dataset.entryId = String(entry.id);
        row.dataset.activeCandidate = entry.isActiveCandidate ? 'true' : 'false';

        const person = makeElement('div', 'sales-history-attendance-row__person');
        const selectionLabel = makeElement('label', 'form-check');
        const selection = makeElement('input', 'form-check-input');
        selection.type = 'checkbox';
        selection.checked = entry.isSelected;
        selection.disabled = !entry.isActiveCandidate && !entry.isSelected;
        selection.dataset.attendanceSelected = '';
        const name = makeElement('span', 'form-check-label');
        name.append(makeElement('strong', '', entry.displayName));
        if (entry.departmentName) {
            name.append(makeElement('small', '', entry.departmentName));
        }
        selectionLabel.append(selection, name);
        person.append(selectionLabel);
        if (!entry.isActiveCandidate) {
            person.append(makeElement('span', 'badge text-bg-secondary', '無効化済み'));
        }

        const fields = makeElement('div', 'sales-history-attendance-row__fields');
        const clockInLabel = makeElement('label');
        clockInLabel.append(makeElement('span', '', '出勤'));
        const clockIn = makeElement('input', 'form-control');
        clockIn.type = 'time';
        clockIn.value = entry.clockInTime;
        clockIn.dataset.attendanceClockIn = '';
        clockInLabel.append(clockIn);

        const clockOutLabel = makeElement('label');
        clockOutLabel.append(makeElement('span', '', '退勤'));
        const clockOut = makeElement('input', 'form-control');
        clockOut.type = 'time';
        clockOut.value = entry.clockOutTime;
        clockOut.dataset.attendanceClockOut = '';
        clockOutLabel.append(clockOut);

        const sendLabel = makeElement('label', 'form-check sales-history-attendance-row__send');
        const send = makeElement('input', 'form-check-input');
        send.type = 'checkbox';
        send.checked = entry.usesSendService;
        send.dataset.attendanceSend = '';
        sendLabel.append(send, makeElement('span', 'form-check-label', '送り'));
        fields.append(clockInLabel, clockOutLabel, sendLabel);
        row.append(person, fields);

        const sync = () => {
            const selected = selection.checked;
            clockIn.disabled = !selected;
            clockOut.disabled = !selected;
            send.disabled = !selected;
            clockIn.required = selected;
            clockOut.required = selected;
        };
        selection.addEventListener('change', () => {
            sync();
            if (entry.type === 'cast') {
                syncRequiredDrinkBacks(true);
            }
        });
        sync();
        return row;
    };

    const updateDrinkBackTotal = () => {
        let total = 0;
        modalElement.querySelectorAll('[data-drink-back-row]').forEach((row) => {
            if (row.querySelector('[data-drink-back-active]').checked) {
                total += asNumber(row.querySelector('[data-drink-back-amount]').value);
            }
        });
        modalElement.querySelector('[data-correction-drink-back-total]').textContent = `合計 ${formatYen(total)}`;
    };

    const syncRequiredDrinkBacks = (activateNewRequirements = false) => {
        const selectedCastIds = new Set(
            [...modalElement.querySelectorAll('[data-attendance-type="cast"]')]
                .filter((row) => row.querySelector('[data-attendance-selected]').checked)
                .map((row) => row.dataset.entryId)
        );
        modalElement.querySelectorAll('[data-drink-back-row]').forEach((row) => {
            const active = row.querySelector('[data-drink-back-active]');
            const amount = row.querySelector('[data-drink-back-amount]');
            const required = selectedCastIds.has(row.dataset.castId);
            const activeCandidate = row.dataset.activeCandidate === 'true';
            if (required && activateNewRequirements) {
                active.checked = true;
                if (amount.value === '') {
                    amount.value = '0';
                }
            }
            active.disabled = required
                ? active.checked
                : (!activeCandidate && !active.checked);
            amount.disabled = !active.checked;
            amount.required = active.checked;
            row.classList.toggle('is-required', required);
            row.querySelector('[data-drink-back-required]').hidden = !required;
        });
        updateDrinkBackTotal();
    };

    const createDrinkBackRow = (entry) => {
        const row = makeElement('div', 'sales-history-drink-row');
        row.dataset.drinkBackRow = '';
        row.dataset.castId = String(entry.castId);
        row.dataset.activeCandidate = entry.isActiveCandidate ? 'true' : 'false';

        const selectionLabel = makeElement('label', 'form-check sales-history-drink-row__person');
        const active = makeElement('input', 'form-check-input');
        active.type = 'checkbox';
        active.checked = entry.isActive;
        active.dataset.drinkBackActive = '';
        const label = makeElement('span', 'form-check-label');
        label.append(makeElement('strong', '', entry.displayName));
        if (entry.departmentName) {
            label.append(makeElement('small', '', entry.departmentName));
        }
        selectionLabel.append(active, label);

        const required = makeElement('span', 'badge text-bg-warning', '出勤者');
        required.dataset.drinkBackRequired = '';
        required.hidden = !entry.isRequired;

        const amountGroup = makeElement('div', 'input-group sales-history-drink-row__amount');
        const amount = makeElement('input', 'form-control');
        amount.type = 'number';
        amount.step = '1';
        amount.min = '-999999999999';
        amount.max = '999999999999';
        amount.inputMode = 'numeric';
        amount.value = entry.adjustmentAmount ?? (active.checked ? 0 : '');
        amount.dataset.drinkBackAmount = '';
        amountGroup.append(amount, makeElement('span', 'input-group-text', '円'));
        row.append(selectionLabel, required, amountGroup);

        active.addEventListener('change', () => {
            amount.disabled = !active.checked;
            amount.required = active.checked;
            if (active.checked && amount.value === '') {
                amount.value = '0';
            }
            updateDrinkBackTotal();
        });
        amount.addEventListener('input', updateDrinkBackTotal);
        return row;
    };

    const createCastSalesSlip = (slip) => {
        const section = makeElement('section', 'sales-history-cast-sales-slip');
        section.dataset.castSalesSlip = '';
        section.dataset.slipId = String(slip.slipId);
        section.dataset.sourceAmountType = slip.sourceAmountType;
        section.dataset.splitMode = slip.splitMode;

        const heading = makeElement('header', 'sales-history-cast-sales-slip__heading');
        const title = makeElement('div');
        title.append(makeElement('strong', '', `${slip.tableCode || slip.tableName || '伝票'} / No.${slip.slipId}`));
        title.append(makeElement('span', '', slip.customerNames || 'お客様名なし'));
        heading.append(title, makeElement('strong', '', `会計 ${formatYen(slip.totalAmount)}`));

        const rows = makeElement('div', 'sales-history-cast-sales-slip__rows');
        let total = 0;
        slip.adjustments.forEach((adjustment) => {
            const row = makeElement('label', 'sales-history-cast-sales-row');
            row.dataset.castSalesAdjustment = '';
            row.dataset.slipCastId = String(adjustment.slipCastId);
            row.append(makeElement('span', '', adjustment.displayName));
            const group = makeElement('span', 'input-group');
            const input = makeElement('input', 'form-control');
            input.type = 'number';
            input.min = '0';
            input.max = '999999999999';
            input.step = '1';
            input.inputMode = 'numeric';
            input.required = adjustment.salesAmount !== null;
            input.value = adjustment.salesAmount ?? '';
            if (adjustment.suggestedSalesAmount !== null) {
                input.placeholder = String(adjustment.suggestedSalesAmount);
            }
            input.dataset.castSalesAmount = '';
            group.append(input, makeElement('span', 'input-group-text', '円'));
            row.append(group);
            rows.append(row);
            total += asNumber(input.value);
        });
        const footer = makeElement('footer', 'sales-history-cast-sales-slip__footer', `配分合計 ${formatYen(total)}`);
        footer.dataset.castSalesTotal = '';
        rows.addEventListener('input', () => {
            const hasEnteredAmount = [...rows.querySelectorAll('[data-cast-sales-amount]')]
                .some((input) => input.value !== '');
            rows.querySelectorAll('[data-cast-sales-amount]').forEach((input) => {
                input.required = hasEnteredAmount;
            });
            const sum = [...rows.querySelectorAll('[data-cast-sales-amount]')]
                .reduce((current, input) => current + asNumber(input.value), 0);
            footer.textContent = `配分合計 ${formatYen(sum)}`;
        });
        section.append(heading, rows, footer);
        return section;
    };

    const renderCorrectionEditor = (editor) => {
        modalElement.querySelector('[data-correction-subtitle]').textContent =
            `${formatDate(editor.businessDate)} / スナップショット v${editor.snapshotVersion ?? '-'}`;
        modalElement.querySelector('[data-correction-business-day-id]').value = String(editor.businessDayId);
        modalElement.querySelector('[data-correction-snapshot-version]').value = String(editor.snapshotVersion ?? '');
        modalElement.querySelector('[data-correction-memo]').value = editor.memo;
        modalElement.querySelector('[data-correction-drink-delivery]').value = String(editor.drinkDeliveryAmount);
        modalElement.querySelector('[data-correction-reason]').value = '';
        modalElement.querySelector('[data-correction-reason-count]').textContent = '0 / 500';

        const castEntries = modalElement.querySelector('[data-correction-cast-entries]');
        const staffEntries = modalElement.querySelector('[data-correction-staff-entries]');
        const drinkBackEntries = modalElement.querySelector('[data-correction-drink-back-entries]');
        const castSalesSlips = modalElement.querySelector('[data-correction-cast-sales-slips]');
        castEntries.replaceChildren(...editor.castEntries.map(createAttendanceRow));
        staffEntries.replaceChildren(...editor.staffEntries.map(createAttendanceRow));
        drinkBackEntries.replaceChildren(...editor.drinkBackAdjustments.map(createDrinkBackRow));
        castSalesSlips.replaceChildren(...editor.castSalesSlips.map(createCastSalesSlip));
        if (editor.castSalesSlips.length === 0) {
            castSalesSlips.append(makeElement('p', 'sales-history-correction__empty', '補正対象のキャスト売上はありません。'));
        }
        syncRequiredDrinkBacks();
        modalElement.querySelector('[data-correction-loading]').hidden = true;
        modalElement.querySelector('[data-correction-editor]').hidden = false;
        modalElement.querySelector('[data-correction-submit]').disabled = false;
        updateCorrectionStatus('保存できます。');
        window.bootstrap?.Tab?.getOrCreateInstance(document.getElementById('correction-basic-tab'))?.show();
    };

    const openCorrectionEditor = async (businessDayId) => {
        if (!businessDayId || !modalElement) {
            return;
        }
        modal?.show();
        state.editor = null;
        state.operationId = crypto.randomUUID();
        setCorrectionError();
        modalElement.querySelector('[data-correction-loading]').hidden = false;
        modalElement.querySelector('[data-correction-editor]').hidden = true;
        modalElement.querySelector('[data-correction-submit]').disabled = true;
        updateCorrectionStatus('取得中', 'saving');
        try {
            const url = new URL(editorUrl, window.location.href);
            url.searchParams.set('businessDayId', String(businessDayId));
            const editor = normalizeEditor(await requestJson(`${url.pathname}${url.search}`));
            if (!editor.canEdit || !editor.snapshotVersion) {
                throw new Error(editor.editReason || 'この営業日は補正できません。');
            }
            state.editor = editor;
            renderCorrectionEditor(editor);
        } catch (requestError) {
            setCorrectionError(requestError instanceof Error
                ? requestError.message
                : '補正対象を取得できませんでした。');
            modalElement.querySelector('[data-correction-loading]').hidden = true;
            updateCorrectionStatus('取得失敗', 'error');
        }
    };

    const collectAttendance = (type) => [...modalElement.querySelectorAll(`[data-attendance-type="${type}"]`)]
        .map((row) => {
            const selected = row.querySelector('[data-attendance-selected]').checked;
            return {
                [`${type}Id`]: asNumber(row.dataset.entryId),
                isSelected: selected,
                clockInTime: selected ? row.querySelector('[data-attendance-clock-in]').value : null,
                clockOutTime: selected ? row.querySelector('[data-attendance-clock-out]').value : null,
                usesSendService: selected && row.querySelector('[data-attendance-send]').checked
            };
        });

    const collectCorrectionMutation = () => ({
        operationId: state.operationId,
        businessDayId: state.editor.businessDayId,
        expectedSnapshotVersion: state.editor.snapshotVersion,
        reason: modalElement.querySelector('[data-correction-reason]').value.trim(),
        memo: modalElement.querySelector('[data-correction-memo]').value,
        drinkDeliveryAmount: asNumber(modalElement.querySelector('[data-correction-drink-delivery]').value),
        castEntries: collectAttendance('cast'),
        staffEntries: collectAttendance('staff'),
        drinkBackAdjustments: [...modalElement.querySelectorAll('[data-drink-back-row]')].map((row) => {
            const active = row.querySelector('[data-drink-back-active]').checked;
            return {
                castId: asNumber(row.dataset.castId),
                isActive: active,
                adjustmentAmount: active ? asNumber(row.querySelector('[data-drink-back-amount]').value) : null
            };
        }),
        castSalesSlips: [...modalElement.querySelectorAll('[data-cast-sales-slip]')].map((slip) => ({
            slipId: asNumber(slip.dataset.slipId),
            sourceAmountType: slip.dataset.sourceAmountType,
            splitMode: slip.dataset.splitMode,
            adjustments: [...slip.querySelectorAll('[data-cast-sales-adjustment]')].map((row) => ({
                slipCastId: asNumber(row.dataset.slipCastId),
                salesAmount: asNullableNumber(row.querySelector('[data-cast-sales-amount]').value)
            }))
        }))
    });

    list.addEventListener('click', (event) => {
        const button = event.target.closest('[data-business-day-id]');
        if (button) {
            selectBusinessDay(asNumber(button.dataset.businessDayId));
        }
    });
    loadMoreButton.addEventListener('click', () => void loadHistoryPage({ append: true }));
    correctionButton.addEventListener('click', () => {
        void openCorrectionEditor(asNumber(correctionButton.dataset.businessDayId));
    });
    filterForm.addEventListener('submit', (event) => {
        event.preventDefault();
        if (fromDateInput.value && toDateInput.value && fromDateInput.value > toDateInput.value) {
            toDateInput.setCustomValidity('終了日は開始日以降を指定してください。');
            toDateInput.reportValidity();
            return;
        }
        toDateInput.setCustomValidity('');
        updateUrl();
        void loadInitialHistory();
    });
    root.querySelector('[data-history-filter-clear]').addEventListener('click', () => {
        fromDateInput.value = '';
        toDateInput.value = '';
        toDateInput.setCustomValidity('');
        updateUrl();
        void loadInitialHistory();
    });
    toDateInput.addEventListener('input', () => toDateInput.setCustomValidity(''));

    correctionForm?.addEventListener('input', (event) => {
        if (event.target.matches('[data-correction-reason]')) {
            modalElement.querySelector('[data-correction-reason-count]').textContent =
                `${event.target.value.length} / 500`;
        }
        if (state.editor) {
            markCorrectionDirty();
        }
    });
    correctionForm?.addEventListener('change', () => {
        if (state.editor) {
            markCorrectionDirty();
        }
    });
    correctionForm?.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (!state.editor) {
            return;
        }
        const invalidControl = correctionForm.querySelector(':invalid');
        if (invalidControl) {
            const pane = invalidControl.closest('.tab-pane');
            const tab = pane
                ? modalElement.querySelector(`[data-bs-target="#${pane.id}"]`)
                : null;
            window.bootstrap?.Tab?.getOrCreateInstance(tab)?.show();
            window.requestAnimationFrame(() => {
                invalidControl.reportValidity();
                invalidControl.focus({ preventScroll: false });
            });
            return;
        }
        const mutation = collectCorrectionMutation();
        if (!mutation.reason) {
            const reason = modalElement.querySelector('[data-correction-reason]');
            reason.setCustomValidity('補正理由を入力してください。');
            reason.reportValidity();
            reason.setCustomValidity('');
            return;
        }

        const submit = modalElement.querySelector('[data-correction-submit]');
        submit.disabled = true;
        setCorrectionError();
        updateCorrectionStatus('保存中', 'saving');
        try {
            const result = await requestJson(saveUrl, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { RequestVerificationToken: token } : {})
                },
                body: JSON.stringify(mutation)
            });
            if (result?.status !== 'confirmed' && result?.status !== 'unchanged') {
                throw new Error(readErrorMessage(result, '補正内容を保存できませんでした。'));
            }
            const unchanged = result.status === 'unchanged';
            updateCorrectionStatus(unchanged ? '変更はありません。' : '補正を保存しました。');
            const businessDayId = mutation.businessDayId;
            await loadInitialHistory(businessDayId);
            window.setTimeout(() => modal?.hide(), 400);
        } catch (requestError) {
            const fallback = requestError?.status === 409
                ? 'ほかの操作で更新されています。閉じて最新状態を再取得してください。'
                : '補正内容を保存できませんでした。';
            setCorrectionError(requestError instanceof Error ? requestError.message || fallback : fallback);
            updateCorrectionStatus('保存失敗', 'error');
            submit.disabled = false;
        }
    });

    const initialUrl = new URL(window.location.href);
    fromDateInput.value = initialUrl.searchParams.get('fromBusinessDate') || '';
    toDateInput.value = initialUrl.searchParams.get('toBusinessDate') || '';
    const initialBusinessDayId = asNullableNumber(initialUrl.searchParams.get('businessDayId'));
    void loadInitialHistory(initialBusinessDayId);
})();
