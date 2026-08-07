(() => {
    const root = document.querySelector('[data-daily-report]');
    if (!root) {
        return;
    }

    const refreshIntervalMs = 30000;
    let reportUrl = root.dataset.reportUrl;
    const status = root.querySelector('[data-report-status]');
    const content = root.querySelector('[data-report-content]');
    const warnings = root.querySelector('[data-report-warnings]');
    const stateBadge = root.querySelector('[data-report-state]');
    const printButton = root.querySelector('[data-report-print]');
    const refreshButton = root.querySelector('[data-report-refresh]');
    let refreshInFlight = false;
    let lastReportState = null;

    const resetPrintLayout = () => {
        root.querySelectorAll('[data-report-page-content]').forEach((pageContent) => {
            pageContent.style.removeProperty('--daily-report-print-scale');
            pageContent.style.removeProperty('width');
        });
    };

    const preparePrintLayout = () => {
        resetPrintLayout();
        root.querySelectorAll('.daily-report__page').forEach((page) => {
            const pageContent = page.querySelector('[data-report-page-content]');
            if (!pageContent) {
                return;
            }

            const pageStyle = window.getComputedStyle(page);
            const verticalPadding =
                (Number.parseFloat(pageStyle.paddingTop) || 0) +
                (Number.parseFloat(pageStyle.paddingBottom) || 0);
            const footer = page.querySelector('.daily-report__footer');
            const footerReserve = footer ? footer.offsetHeight + 24 : 0;
            const availableHeight = page.clientHeight - verticalPadding - footerReserve;
            const contentHeight = pageContent.scrollHeight;
            if (availableHeight <= 0 || contentHeight <= availableHeight) {
                return;
            }

            const scale = availableHeight / contentHeight;
            pageContent.style.setProperty('--daily-report-print-scale', `${scale}`);
            pageContent.style.width = `${100 / scale}%`;
        });
    };

    const yenFormatter = new Intl.NumberFormat('ja-JP', {
        style: 'currency',
        currency: 'JPY',
        maximumFractionDigits: 0
    });
    const numberFormatter = new Intl.NumberFormat('ja-JP', {
        maximumFractionDigits: 2
    });
    const dateFormatter = new Intl.DateTimeFormat('ja-JP', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit'
    });
    const dateTimeFormatter = new Intl.DateTimeFormat('ja-JP', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
    });
    const timeFormatter = new Intl.DateTimeFormat('ja-JP', {
        hour: '2-digit',
        minute: '2-digit'
    });
    const sheetDateFormatter = new Intl.DateTimeFormat('ja-JP-u-ca-japanese', {
        era: 'long',
        year: 'numeric',
        month: 'numeric',
        day: 'numeric',
        weekday: 'short'
    });

    const toYen = (value) => value === null || value === undefined
        ? '旧形式では未保存'
        : yenFormatter.format(Number(value) || 0);
    const toNumber = (value) => numberFormatter.format(Number(value) || 0);
    const toDate = (value) => {
        if (!value) {
            return '-';
        }

        const parsed = new Date(`${value}T00:00:00+09:00`);
        return Number.isNaN(parsed.valueOf()) ? value : dateFormatter.format(parsed);
    };
    const toSheetDate = (value) => {
        if (!value) {
            return '-';
        }

        const parsed = new Date(`${value}T00:00:00+09:00`);
        return Number.isNaN(parsed.valueOf()) ? value : sheetDateFormatter.format(parsed);
    };
    const toTime = (value) => {
        if (!value) {
            return '-';
        }

        const parsed = new Date(value);
        return Number.isNaN(parsed.valueOf()) ? '-' : timeFormatter.format(parsed);
    };

    const makeCell = (text, className = '') => {
        const cell = document.createElement('td');
        cell.textContent = text ?? '-';
        if (className) {
            cell.className = className;
        }
        return cell;
    };

    const renderRows = (selector, rows, rowFactory, emptyMessage, columnCount) => {
        const body = root.querySelector(selector);
        if (!body) {
            return;
        }

        body.replaceChildren();
        const sourceRows = rows ?? [];
        if (!sourceRows.length) {
            const row = document.createElement('tr');
            const cell = makeCell(emptyMessage, 'text-muted');
            cell.colSpan = columnCount;
            row.append(cell);
            body.append(row);
        } else {
            sourceRows.forEach((item, index) => body.append(rowFactory(item, index)));
        }

        const minimumRows = Number(body.dataset.minRows) || 0;
        while (body.children.length < minimumRows) {
            const row = document.createElement('tr');
            row.className = 'daily-report__blank-row';
            for (let index = 0; index < columnCount; index += 1) {
                row.append(makeCell(''));
            }
            body.append(row);
        }
    };

    const setTotal = (name, value) => {
        const element = root.querySelector(`[data-report-total="${name}"]`);
        if (element) {
            element.textContent = value;
        }
    };

    const setCount = (name, value) => {
        const element = root.querySelector(`[data-report-count="${name}"]`);
        if (element) {
            element.textContent = `${Number(value) || 0}`;
        }
    };

    const renderWarnings = (items) => {
        if (!warnings) {
            return;
        }

        warnings.replaceChildren();
        warnings.hidden = !items?.length;
        items?.forEach((message) => {
            const alert = document.createElement('div');
            alert.className = 'alert alert-warning';
            alert.setAttribute('role', 'alert');
            alert.textContent = message;
            warnings.append(alert);
        });
    };

    const renderReportState = (report) => {
        const definitions = {
            provisional: ['締め前（暫定）', 'text-bg-warning'],
            closed: ['締め済み（確定）', 'text-bg-success'],
            legacy: ['締め済み（旧形式）', 'text-bg-secondary']
        };
        const definition = definitions[report.state] ?? ['日報', 'text-bg-secondary'];
        stateBadge.className = `badge ${definition[1]}`;
        stateBadge.textContent = definition[0];
        lastReportState = report.state;
    };

    const render = (report) => {
        const businessDay = report.businessDay ?? {};
        const totals = report.totals ?? {};
        const unavailable = new Set(report.legacyUnavailableSections ?? []);

        renderReportState(report);
        const departmentName = businessDay.departmentName || '店舗';
        root.querySelector('[data-report-subtitle]').textContent =
            `${departmentName}　${toDate(businessDay.businessDate)}`;
        root.querySelectorAll('[data-report-department]').forEach((element) => {
            element.textContent = departmentName;
        });
        root.querySelectorAll('[data-report-sheet-date]').forEach((element) => {
            element.textContent = toSheetDate(businessDay.businessDate);
        });

        setTotal('sales', toYen(totals.salesAmount));
        setTotal('cash', toYen(totals.cashAmount));
        setTotal('expense', toYen(totals.expenseAmount));
        setTotal('balance', toYen(totals.cashBalanceAmount));
        setTotal('drink', toYen(totals.drinkDeliveryAmount));
        setCount('attendance', Array.isArray(report.casts) ? report.casts.length : 0);
        setCount('groups', totals.slipCount);
        setCount('customers', totals.customerCount);

        renderRows(
            '[data-report-payments]',
            report.payments,
            (payment) => {
                const row = document.createElement('tr');
                row.append(
                    makeCell(payment.name || payment.code),
                    makeCell(toYen(payment.amount), 'text-end')
                );
                return row;
            },
            '確定した支払はありません。',
            2
        );

        renderRows(
            '[data-report-visits]',
            report.visits,
            (visit, index) => {
                const row = document.createElement('tr');
                const paymentText = (visit.payments ?? [])
                    .map((payment) => `${payment.name || payment.code} ${toYen(payment.amount)}`)
                    .join(' / ');
                const note = [paymentText, visit.memo, visit.statusDisplay || visit.status]
                    .filter((value) => value?.trim())
                    .join(' / ');
                row.append(
                    makeCell(visit.slipNo || `${index + 1}`.padStart(2, '0')),
                    makeCell(visit.entryTime),
                    makeCell(visit.tableDisplay),
                    makeCell(`${Number(visit.customerCount) || 0}`),
                    makeCell(visit.customerNames),
                    makeCell(visit.castNames || '-'),
                    makeCell(toYen(visit.amount), 'text-end'),
                    makeCell(note || '-')
                );
                return row;
            },
            '伝票はありません。',
            8
        );

        renderRows(
            '[data-report-casts]',
            report.casts,
            (cast) => {
                const row = document.createElement('tr');
                row.append(
                    makeCell(cast.displayName),
                    makeCell(toTime(cast.clockInAt)),
                    makeCell(toTime(cast.clockOutAt)),
                    makeCell(toYen(cast.drinkBackAmount), 'text-end'),
                    makeCell(toYen(cast.assignmentBackAmount), 'text-end'),
                    makeCell(toYen(cast.castSalesAmount), 'text-end'),
                    makeCell(toYen(cast.advanceAmount), 'text-end'),
                    makeCell(cast.usesSendService ? '利用' : '-')
                );
                return row;
            },
            '出勤キャストはいません。',
            8
        );

        const staffs = Array.isArray(report.staffs) ? report.staffs : [];
        const employeeStaffs = staffs.filter((staff) => staff.employmentType !== 'part_time');
        const partTimeStaffs = staffs.filter((staff) => staff.employmentType === 'part_time');
        const renderStaffs = (selector, rows, emptyMessage) => renderRows(
            selector,
            rows,
            (staff) => {
                const row = document.createElement('tr');
                row.append(
                    makeCell(staff.displayName),
                    makeCell(toTime(staff.clockInAt)),
                    makeCell(toTime(staff.clockOutAt))
                );
                return row;
            },
            emptyMessage,
            3
        );
        renderStaffs('[data-report-staffs="employee"]', employeeStaffs, '出勤社員はいません。');
        renderStaffs('[data-report-staffs="part_time"]', partTimeStaffs, '出勤バイトはいません。');

        renderRows(
            '[data-report-expenses]',
            report.expenseAccounts,
            (expense) => {
                const row = document.createElement('tr');
                row.append(
                    makeCell(expense.accountName || expense.accountCode),
                    makeCell(toYen(expense.amount), 'text-end')
                );
                return row;
            },
            unavailable.has('expenses') ? '旧形式では未保存です。' : '当日支払の領収書支出はありません。',
            2
        );

        root.querySelector('[data-report-memo]').textContent =
            businessDay.memo?.trim() || 'なし';
        const captured = report.capturedAt ? new Date(report.capturedAt) : null;
        root.querySelectorAll('[data-report-captured]').forEach((element) => {
            element.textContent = captured && !Number.isNaN(captured.valueOf())
                ? `作成時刻 ${dateTimeFormatter.format(captured)}`
                : '';
        });

        renderWarnings(report.warnings ?? []);
        status.hidden = true;
        content.hidden = false;
        printButton.disabled = false;
        root.setAttribute('aria-busy', 'false');
    };

    const setFailure = (message) => {
        status.hidden = false;
        status.className = 'alert alert-danger daily-report__loading';
        status.textContent = message;
        if (lastReportState === null) {
            content.hidden = true;
            printButton.disabled = true;
        }
        root.setAttribute('aria-busy', 'false');
    };

    const load = async () => {
        if (refreshInFlight || !reportUrl) {
            return;
        }

        refreshInFlight = true;
        refreshButton.disabled = true;
        root.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(reportUrl, {
                headers: { Accept: 'application/json' },
                cache: 'no-store'
            });
            if (!response.ok) {
                let message = '日報を取得できませんでした。';
                try {
                    const error = await response.json();
                    if (error.errorMessage) {
                        message = error.errorMessage;
                    }
                } catch {
                    // JSON以外の失敗応答では既定文言を表示します。
                }
                throw new Error(message);
            }

            render(await response.json());
        } catch (error) {
            setFailure(error instanceof Error ? error.message : '日報を取得できませんでした。');
        } finally {
            refreshButton.disabled = false;
            refreshInFlight = false;
        }
    };

    refreshButton.addEventListener('click', () => void load());
    window.addEventListener('prosper:daily-report-business-day', (event) => {
        const businessDayId = Number(event.detail?.businessDayId) || 0;
        if (businessDayId <= 0) {
            return;
        }

        const url = new URL(reportUrl, window.location.origin);
        url.searchParams.set('businessDayId', String(businessDayId));
        reportUrl = `${url.pathname}${url.search}`;
        void load();
    });
    printButton.addEventListener('click', () => {
        preparePrintLayout();
        window.print();
    });
    window.addEventListener('beforeprint', preparePrintLayout);
    window.addEventListener('afterprint', resetPrintLayout);
    window.addEventListener('focus', () => {
        if (document.visibilityState === 'visible' && lastReportState !== 'closed' && lastReportState !== 'legacy') {
            void load();
        }
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible' && lastReportState !== 'closed' && lastReportState !== 'legacy') {
            void load();
        }
    });
    window.setInterval(() => {
        if (document.visibilityState === 'visible' && lastReportState === 'provisional') {
            void load();
        }
    }, refreshIntervalMs);

    void load();
})();
