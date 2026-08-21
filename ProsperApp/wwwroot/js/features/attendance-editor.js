(() => {
    const config = window.prosperAttendanceEditor;
    const form = document.querySelector('[data-attendance-form="true"]');
    if (!config || !form) {
        return;
    }

    const editor = document.getElementById('attendanceEditor');
    const syncStatus = document.getElementById('attendanceSyncStatus');
    const dirtyDecision = document.getElementById('attendanceDirtyDecision');
    const resultMessage = document.getElementById('attendanceResultMessage');
    const lastUpdated = document.getElementById('attendanceLastUpdated');
    const discardButton = document.getElementById('discardAttendanceChanges');
    const addCastButton = document.getElementById('addAttendanceCastButton');
    const addStaffButton = document.getElementById('addAttendanceStaffButton');
    const saveButton = document.getElementById('saveAttendanceButton');
    const selectedCount = document.getElementById('selectedAttendanceCount');
    const emptyMessage = document.getElementById('attendanceEmptyMessage');
    const modalElement = document.getElementById('castSelectModal');
    const modalTitle = document.getElementById('castSelectModalLabel');
    const modalList = document.getElementById('castModalList');
    const searchInput = document.getElementById('castSearchInput');
    const homeStoreFilter = document.getElementById('homeStoreCastFilter');
    const addSelectedButton = document.getElementById('addSelectedAttendanceButton');
    const modal = modalElement ? new bootstrap.Modal(modalElement) : null;
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const saveResponse = window.ProsperSaveResponse;
    const rowByKey = new Map();

    let hydrated = false;
    let applying = false;
    let dirty = false;
    let saving = false;
    let loading = false;
    let pendingCommand = null;
    let businessDayId = null;
    let businessDayRevision = null;
    let businessDayDate = null;
    let pendingSnapshot = null;
    let modalPersonType = 'cast';
    let lastSynchronizedAt = 0;

    const normalizePersonType = (value) => value === 'staff' ? 'staff' : 'cast';
    const personKey = (type, id) => `${normalizePersonType(type)}:${Number(id)}`;
    const rows = () => Array.from(editor?.querySelectorAll('[data-attendance-row]') || []);
    const isSelected = (row) => row?.dataset.selected === 'true';
    const isRegistered = (row) => row?.dataset.registered === 'true';
    const clockIn = (row) => row.querySelector('[data-clock-in-select]');
    const clockOut = (row) => row.querySelector('[data-clock-out-select]');
    const sendService = (row) => row.querySelector('[data-send-service]');
    const removeButton = (row) => row.querySelector('[data-remove-attendance]');

    const randomHex = (length) => Array.from(
        window.crypto?.getRandomValues(new Uint8Array(Math.ceil(length / 2))) ||
        Array.from({ length: Math.ceil(length / 2) }, () => Math.floor(Math.random() * 256)),
        (value) => value.toString(16).padStart(2, '0'))
        .join('')
        .slice(0, length);
    const newOperationId = () => window.crypto?.randomUUID
        ? window.crypto.randomUUID()
        : `${randomHex(8)}-${randomHex(4)}-4${randomHex(3)}-8${randomHex(3)}-${randomHex(12)}`;
    const pendingCommandKey = `prosper:attendance-mutation:v2:${config.departmentId || 'current'}`;
    const readPendingCommand = () => {
        try {
            const command = JSON.parse(sessionStorage.getItem(pendingCommandKey) || 'null');
            return command?.operationId && Array.isArray(command.entries) ? command : null;
        } catch {
            return null;
        }
    };
    const writePendingCommand = () => {
        try {
            if (pendingCommand) sessionStorage.setItem(pendingCommandKey, JSON.stringify(pendingCommand));
            else sessionStorage.removeItem(pendingCommandKey);
        } catch {
            // The in-memory command remains available for this page lifetime.
        }
    };
    const clearPendingCommand = () => {
        pendingCommand = null;
        writePendingCommand();
    };
    pendingCommand = readPendingCommand();

    const setResult = (message, kind = 'danger') => {
        if (!resultMessage) {
            return;
        }

        resultMessage.textContent = message || '';
        resultMessage.className = `alert alert-${kind}`;
        resultMessage.hidden = !message;
    };

    const setSyncStatus = (message, kind = 'secondary') => {
        if (!syncStatus) {
            return;
        }

        syncStatus.textContent = message;
        syncStatus.className = `alert alert-${kind}`;
        syncStatus.hidden = false;
    };

    const updateLastSynchronized = () => {
        lastSynchronizedAt = Date.now();
        if (!lastUpdated) {
            return;
        }

        lastUpdated.textContent = `最終更新: ${new Date().toLocaleString('ja-JP')}`;
        lastUpdated.hidden = false;
    };

    const appendOptions = (select, options, emptyLabel) => {
        const empty = document.createElement('option');
        empty.value = '';
        empty.textContent = emptyLabel;
        select.appendChild(empty);
        (options || []).forEach((option) => {
            const element = document.createElement('option');
            element.value = option.value || '';
            element.textContent = option.label || option.value || '';
            select.appendChild(element);
        });
    };

    const normalizedSelectValue = (value) => (value ?? '').toString().trim();

    const ensureOption = (select, value, label = value) => {
        const normalized = normalizedSelectValue(value);
        if (!select || !normalized) {
            return normalized;
        }

        if (Array.from(select.options).some((option) => option.value === normalized)) {
            return normalized;
        }

        const option = document.createElement('option');
        option.value = normalized;
        option.textContent = normalizedSelectValue(label) || normalized;
        option.dataset.persistedValue = 'true';
        select.appendChild(option);
        return normalized;
    };

    const setSelectValue = (select, value, fallback = '') => {
        const normalized = normalizedSelectValue(value);
        if (normalized) {
            select.value = ensureOption(select, normalized);
            return;
        }

        select.value = normalizedSelectValue(fallback);
    };

    const createDynamicRow = (entry) => {
        if (!editor || !emptyMessage) {
            return null;
        }

        const row = document.createElement('div');
        row.className = 'closing-attendance-table__row';
        row.dataset.attendanceRow = '';
        row.dataset.personType = normalizePersonType(entry.personType);
        row.dataset.personId = String(entry.personId || 0);
        row.dataset.displayName = entry.displayName || '';
        row.dataset.departmentName = entry.departmentName || '';
        row.dataset.isHome = 'false';
        row.dataset.selected = 'false';
        row.dataset.registered = 'false';
        row.dataset.attendanceId = '0';
        row.dataset.version = '';

        const person = document.createElement('span');
        person.className = 'closing-attendance-table__cast';
        person.append(document.createTextNode(entry.displayName || `ID ${entry.personId}`));
        if (entry.departmentName) {
            const department = document.createElement('small');
            department.textContent = entry.departmentName;
            person.appendChild(department);
        }
        const type = document.createElement('small');
        type.textContent = normalizePersonType(entry.personType) === 'staff' ? 'スタッフ' : 'キャスト';
        person.appendChild(type);
        const registration = document.createElement('small');
        registration.dataset.attendanceRegistration = '';
        person.appendChild(registration);

        const clockInLabel = document.createElement('label');
        clockInLabel.className = 'closing-attendance-table__field';
        const clockInText = document.createElement('span');
        clockInText.textContent = '出勤時刻';
        const clockInSelect = document.createElement('select');
        clockInSelect.className = 'form-select';
        clockInSelect.dataset.clockInSelect = '';
        appendOptions(clockInSelect, config.clockInOptions, '選択');
        clockInLabel.append(clockInText, clockInSelect);

        const clockOutLabel = document.createElement('label');
        clockOutLabel.className = 'closing-attendance-table__field';
        const clockOutText = document.createElement('span');
        clockOutText.textContent = '退勤時刻';
        const clockOutSelect = document.createElement('select');
        clockOutSelect.className = 'form-select';
        clockOutSelect.dataset.clockOutSelect = '';
        appendOptions(clockOutSelect, config.clockOutOptions, '未入力');
        clockOutLabel.append(clockOutText, clockOutSelect);

        const sendLabel = document.createElement('label');
        sendLabel.className = 'closing-attendance-table__send form-check';
        const send = document.createElement('input');
        send.className = 'form-check-input';
        send.type = 'checkbox';
        send.dataset.sendService = '';
        const sendText = document.createElement('span');
        sendText.className = 'form-check-label';
        sendText.textContent = '利用';
        sendLabel.append(send, sendText);

        const remove = document.createElement('button');
        remove.className = 'btn btn-outline-danger btn-sm closing-attendance-table__remove';
        remove.type = 'button';
        remove.dataset.removeAttendance = '';
        remove.textContent = '削除';

        row.append(person, clockInLabel, clockOutLabel, sendLabel, remove);
        editor.insertBefore(row, emptyMessage);
        bindRow(row);
        return row;
    };

    const updateControls = () => {
        const selectedRows = rows().filter(isSelected);
        if (selectedCount) {
            selectedCount.textContent = `${selectedRows.length} 名`;
        }
        if (emptyMessage) {
            emptyMessage.hidden = !hydrated || selectedRows.length > 0;
        }

        rows().forEach((row) => {
            const enabled = hydrated && !saving;
            clockIn(row).disabled = !enabled;
            clockOut(row).disabled = !enabled;
            sendService(row).disabled = !enabled;
            removeButton(row).disabled = !enabled || !isSelected(row);
        });
        if (addCastButton) {
            addCastButton.disabled = !hydrated || saving;
        }
        if (addStaffButton) {
            addStaffButton.disabled = !hydrated || saving;
        }
        if (saveButton) {
            saveButton.disabled = !hydrated || saving || selectedRows.length === 0;
        }
        if (editor) {
            editor.setAttribute('aria-busy', (!hydrated || saving).toString());
        }
    };

    const markDirty = () => {
        if (!hydrated || applying) {
            return;
        }

        dirty = true;
        if (dirtyDecision) {
            dirtyDecision.hidden = true;
        }
        setResult('');
        updateControls();
    };

    const setSelected = (row, selected, markAsDirty = true) => {
        row.dataset.selected = selected ? 'true' : 'false';
        row.hidden = !selected;
        if (selected && !clockIn(row).value) {
            setSelectValue(clockIn(row), config.defaultClockInTime);
        }
        if (selected && !clockOut(row).value) {
            setSelectValue(clockOut(row), config.defaultClockOutTime);
        }
        if (markAsDirty) {
            markDirty();
        }
        updateControls();
    };

    function bindRow(row) {
        const key = personKey(row.dataset.personType, row.dataset.personId);
        rowByKey.set(key, row);
        setSelectValue(clockIn(row), config.defaultClockInTime);
        setSelectValue(clockOut(row), config.defaultClockOutTime);
        clockIn(row).addEventListener('change', markDirty);
        clockOut(row).addEventListener('change', markDirty);
        sendService(row).addEventListener('change', markDirty);
        removeButton(row).addEventListener('click', () => setSelected(row, false));
    }

    rows().forEach(bindRow);

    const normalizeSnapshot = (value) => value?.snapshot || value;

    const applySnapshot = (value, force = false) => {
        const snapshot = normalizeSnapshot(value);
        if (!snapshot) {
            return false;
        }
        if (snapshot.unchanged) {
            hydrated = true;
            updateLastSynchronized();
            updateControls();
            return true;
        }
        if (dirty && !force) {
            pendingSnapshot = snapshot;
            if (dirtyDecision) {
                dirtyDecision.hidden = false;
            }
            return false;
        }

        applying = true;
        try {
            businessDayId = snapshot.businessDay?.businessDayId || null;
            businessDayRevision = snapshot.hasBusinessDay
                ? Number(snapshot.businessDayRevision ?? snapshot.businessDay?.businessUiRevision ?? 0)
                : null;
            businessDayDate = snapshot.hasBusinessDay
                ? (snapshot.businessDay?.businessDate || snapshot.businessDate || null)
                : null;
            if (pendingCommand && !pendingCommand.businessDate && businessDayDate &&
                (!pendingCommand.businessDayId || Number(pendingCommand.businessDayId) === Number(businessDayId))) {
                pendingCommand.businessDate = businessDayDate;
                writePendingCommand();
            }

            rows().forEach((row) => {
                row.dataset.selected = 'false';
                row.dataset.registered = 'false';
                row.dataset.attendanceId = '0';
                row.dataset.version = '';
                row.hidden = true;
                setSelectValue(clockIn(row), config.defaultClockInTime);
                setSelectValue(clockOut(row), config.defaultClockOutTime);
                sendService(row).checked = false;
                const registration = row.querySelector('[data-attendance-registration]');
                if (registration) {
                    registration.textContent = '';
                }
            });

            (snapshot.attendanceEntries || []).forEach((entry) => {
                const key = personKey(entry.personType, entry.personId);
                const row = rowByKey.get(key) || createDynamicRow(entry);
                if (!row) {
                    return;
                }

                row.dataset.selected = 'true';
                row.dataset.registered = 'true';
                row.dataset.attendanceId = String(entry.attendanceId || 0);
                row.dataset.version = entry.version || '';
                row.hidden = false;
                setSelectValue(clockIn(row), entry.clockInTime, config.defaultClockInTime);
                setSelectValue(clockOut(row), entry.clockOutTime, config.defaultClockOutTime);
                sendService(row).checked = Boolean(entry.usesSendService);
                const registration = row.querySelector('[data-attendance-registration]');
                if (registration) {
                    registration.textContent = '登録済み';
                }
            });

            window.ProsperCurrentAttendanceCasts = snapshot.attendingCasts || [];
            window.dispatchEvent(new CustomEvent('prosper:attendance-casts-updated', {
                detail: { casts: window.ProsperCurrentAttendanceCasts }
            }));
            hydrated = true;
            dirty = false;
            pendingSnapshot = null;
            if (dirtyDecision) {
                dirtyDecision.hidden = true;
            }
            setSyncStatus(
                snapshot.hasBusinessDay
                    ? '現在営業日の勤怠を表示しています。'
                    : '営業日は開始されていません。勤怠保存時に現在の業務日で開始します。',
                snapshot.hasBusinessDay ? 'success' : 'warning');
            updateLastSynchronized();
            updateControls();
            return true;
        } finally {
            applying = false;
        }
    };

    const fetchCurrent = async () => {
        if (loading || saving) {
            return;
        }
        if (dirty) {
            if (dirtyDecision) {
                dirtyDecision.hidden = false;
            }
            return;
        }

        loading = true;
        if (!hydrated) {
            setSyncStatus('現在の勤怠を同期しています。');
        }
        try {
            const url = new URL(config.currentUrl, window.location.href);
            if (businessDayId) {
                url.searchParams.set('knownBusinessDayId', String(businessDayId));
            }
            if (businessDayRevision !== null) {
                url.searchParams.set('knownBusinessDayRevision', String(businessDayRevision));
            }
            const response = await fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                cache: 'no-store',
                headers: { Accept: 'application/json' }
            });
            const body = await response.json().catch(() => null);
            if (!response.ok || !body) {
                throw new Error(body?.message || '現在の勤怠入力を取得できませんでした。');
            }
            applySnapshot(body);
        } catch (error) {
            setResult(error instanceof Error ? error.message : '現在の勤怠入力を取得できませんでした。');
            if (!hydrated) {
                setSyncStatus('同期に失敗したため保存できません。再試行してください。', 'danger');
            }
        } finally {
            loading = false;
            updateControls();
        }
    };

    const serializeEntries = () => rows()
        .filter((row) => isSelected(row) || isRegistered(row))
        .map((row) => {
            const type = normalizePersonType(row.dataset.personType);
            const id = Number(row.dataset.personId || 0);
            return {
                personType: type,
                castId: type === 'cast' ? id : 0,
                staffId: type === 'staff' ? id : 0,
                attendanceId: Number(row.dataset.attendanceId || 0),
                expectedVersion: row.dataset.version || null,
                displayName: row.dataset.displayName || '',
                departmentName: row.dataset.departmentName || null,
                isSelected: isSelected(row),
                isRegistered: isRegistered(row),
                clockInTime: clockIn(row).value || '',
                clockOutTime: clockOut(row).value || null,
                usesSendService: sendService(row).checked
            };
        });

    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (!hydrated || saving) {
            return;
        }

        if (!pendingCommand) {
            const entries = serializeEntries();
            const selectedEntries = entries.filter((entry) => entry.isSelected);
            if (selectedEntries.length === 0) {
                setResult('出勤者を1名以上選択してください。', 'warning');
                return;
            }
            if (selectedEntries.some((entry) => !entry.clockInTime)) {
                setResult('出勤時刻を確認してください。', 'warning');
                return;
            }

            pendingCommand = {
                operationId: newOperationId(),
                businessDayId,
                businessDayRevision,
                businessDate: businessDayDate,
                entries
            };
            writePendingCommand();
        }

        saving = true;
        updateControls();
        setResult('');
        try {
            const response = await fetch(config.saveUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/json',
                    ...(token ? { RequestVerificationToken: token } : {})
                },
                body: JSON.stringify(pendingCommand)
            });
            const body = await response.json().catch(() => null);
            const classification = saveResponse?.classify({ response, payload: body }) ?? {
                confirmed: body?.status === 'confirmed',
                rejected: ['conflict', 'validation_error', 'permission_denied', 'stale_work_item'].includes(body?.status),
                retry: !body || response.status >= 500 || body?.status === 'unavailable'
            };

            if (classification.confirmed) {
                clearPendingCommand();
                applySnapshot(body.snapshot, true);
                setResult(
                    body.message || `勤怠入力を保存しました。出勤 ${body.savedCount || 0}名 / 退勤 ${body.savedClockOutCount || 0}名`,
                    'success');
                window.dispatchEvent(new CustomEvent('prosper:attendance-editor-saved', {
                    detail: {
                        snapshot: body.snapshot,
                        savedCount: body.savedCount || 0,
                        savedClockOutCount: body.savedClockOutCount || 0
                    }
                }));
                return;
            }

            if (classification.rejected) {
                clearPendingCommand();
                pendingSnapshot = body?.snapshot || null;
                if (body?.status === 'conflict' && dirtyDecision) {
                    dirtyDecision.hidden = false;
                }
                const rowMessages = (body?.rowResults || [])
                    .map((row) => row.message)
                    .filter(Boolean);
                setResult(
                    rowMessages.join(' ') ||
                    body?.message ||
                    (body?.status === 'conflict' ? '別の更新が先に保存されています。' : '勤怠入力はDB側で拒否されました。'),
                    'warning');
                return;
            }

            setResult(body?.message || '通信結果を確認できませんでした。同じ操作IDで再送します。', 'warning');
        } catch {
            setResult('通信結果を確認できませんでした。同じ操作IDで再送します。', 'warning');
        } finally {
            saving = false;
            updateControls();
        }
    });

    const normalize = (value) => (value || '').toString().trim().toLocaleLowerCase('ja-JP');

    const renderModal = () => {
        if (!modalList) {
            return;
        }
        const query = normalize(searchInput?.value);
        const candidates = rows().filter((row) => normalizePersonType(row.dataset.personType) === modalPersonType);
        const hasHomeCandidates = candidates.some((row) => row.dataset.isHome === 'true');
        const onlyHome = homeStoreFilter?.checked ?? true;
        const matches = candidates
            .filter((row) => !isSelected(row))
            .filter((row) => !onlyHome || !hasHomeCandidates || row.dataset.isHome === 'true')
            .filter((row) => !query || normalize(row.dataset.displayName).includes(query) || normalize(row.dataset.departmentName).includes(query))
            .slice(0, 80);

        modalList.replaceChildren();
        if (addSelectedButton) {
            addSelectedButton.disabled = true;
        }
        if (matches.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'text-muted mb-0';
            empty.textContent = modalPersonType === 'staff'
                ? '追加できるスタッフがいません。'
                : '追加できるキャストがいません。';
            modalList.appendChild(empty);
            return;
        }

        matches.forEach((row) => {
            const item = document.createElement('label');
            item.className = 'closing-attendance-modal__item';
            const checkbox = document.createElement('input');
            checkbox.className = 'form-check-input';
            checkbox.type = 'checkbox';
            checkbox.value = personKey(row.dataset.personType, row.dataset.personId);
            const text = document.createElement('span');
            text.className = 'closing-attendance-modal__item-text';
            const name = document.createElement('strong');
            name.textContent = row.dataset.displayName || '';
            const department = document.createElement('span');
            department.textContent = row.dataset.departmentName || '';
            text.append(name, department);
            item.append(checkbox, text);
            checkbox.addEventListener('change', () => {
                if (addSelectedButton) {
                    addSelectedButton.disabled = !modalList.querySelector('input:checked');
                }
            });
            modalList.appendChild(item);
        });
    };

    const openModal = (personType) => {
        modalPersonType = normalizePersonType(personType);
        if (modalTitle) {
            modalTitle.textContent = modalPersonType === 'staff' ? '出勤スタッフを追加' : '出勤キャストを追加';
        }
        if (addSelectedButton) {
            addSelectedButton.textContent = modalPersonType === 'staff'
                ? '選択したスタッフを追加'
                : '選択したキャストを追加';
        }
        if (searchInput) {
            searchInput.value = '';
        }
        renderModal();
        modal?.show();
    };

    addCastButton?.addEventListener('click', () => openModal('cast'));
    addStaffButton?.addEventListener('click', () => openModal('staff'));
    searchInput?.addEventListener('input', renderModal);
    homeStoreFilter?.addEventListener('change', renderModal);
    modalElement?.addEventListener('shown.bs.modal', () => searchInput?.focus());
    addSelectedButton?.addEventListener('click', () => {
        modalList?.querySelectorAll('input:checked').forEach((checkbox) => {
            const row = rowByKey.get(checkbox.value);
            if (row) {
                setSelected(row, true);
            }
        });
        modal?.hide();
    });

    discardButton?.addEventListener('click', () => {
        dirty = false;
        clearPendingCommand();
        if (pendingSnapshot) {
            const snapshot = pendingSnapshot;
            pendingSnapshot = null;
            applySnapshot(snapshot, true);
            setResult('現在の勤怠を再読込しました。', 'success');
            return;
        }
        fetchCurrent();
    });

    const refreshAfterFocus = () => {
        if (!hydrated || Date.now() - lastSynchronizedAt < 1000) {
            return;
        }
        if (dirty) {
            if (dirtyDecision) {
                dirtyDecision.hidden = false;
            }
            return;
        }
        fetchCurrent();
    };
    window.addEventListener('focus', refreshAfterFocus);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            refreshAfterFocus();
        }
    });

    updateControls();
    if (config.initialSnapshot) {
        applySnapshot(config.initialSnapshot, true);
    } else {
        fetchCurrent();
    }
})();
