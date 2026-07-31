(() => {
    const root = document.querySelector('[data-daily-report]');
    if (!root) {
        return;
    }

    const refreshIntervalMs = 30000;
    const reportUrl = root.dataset.reportUrl;
    const status = root.querySelector('[data-report-status]');
    const content = root.querySelector('[data-report-content]');
    const warnings = root.querySelector('[data-report-warnings]');
    const stateBadge = root.querySelector('[data-report-state]');
    const printButton = root.querySelector('[data-report-print]');
    const refreshButton = root.querySelector('[data-report-refresh]');
    let refreshInFlight = false;
    let lastReportState = null;

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

    const renderRows = (selector, rows, rowFactory, emptyMessage) => {
        const body = root.querySelector(selector);
        if (!body) {
            return;
        }

        body.replaceChildren();
        if (!rows?.length) {
            const row = document.createElement('tr');
            const cell = makeCell(emptyMessage, 'text-muted');
            cell.colSpan = 12;
            row.append(cell);
            body.append(row);
            return;
        }

        rows.forEach((item) => body.append(rowFactory(item)));
    };

    const setTotal = (name, value) => {
        const element = root.querySelector(`[data-report-total="${name}"]`);
        if (element) {
            element.textContent = value;
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
        root.querySelector('[data-report-subtitle]').textContent =
            `${businessDay.departmentName || '店舗'}　${toDate(businessDay.businessDate)}`;

        setTotal('sales', toYen(totals.salesAmount));
        setTotal('cash', toYen(totals.cashAmount));
        setTotal('expense', toYen(totals.expenseAmount));
        setTotal('balance', toYen(totals.cashBalanceAmount));
        setTotal('drink', toYen(totals.drinkDeliveryAmount));
        setTotal(
            'counts',
            `${Number(totals.confirmedCheckoutCount) || 0}会計 / ${Number(totals.slipCount) || 0}伝票 / ${Number(totals.customerCount) || 0}名`
        );

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
            '確定した支払はありません。'
        );

        renderRows(
            '[data-report-categories]',
            report.itemCategories,
            (category) => {
                const row = document.createElement('tr');
                row.append(
                    makeCell(category.name || category.code),
                    makeCell(toNumber(category.quantity), 'text-end'),
                    makeCell(toYen(category.amount), 'text-end')
                );
                return row;
            },
            unavailable.has('itemCategories') ? '旧形式では未保存です。' : '確定会計の商品はありません。'
        );

        renderRows(
            '[data-report-visits]',
            report.visits,
            (visit) => {
                const row = document.createElement('tr');
                const paymentText = (visit.payments ?? [])
                    .map((payment) => `${payment.name || payment.code} ${toYen(payment.amount)}`)
                    .join(' / ');
                row.append(
                    makeCell(visit.entryTime),
                    makeCell(visit.tableDisplay),
                    makeCell(`${Number(visit.customerCount) || 0}名`),
                    makeCell(visit.customerNames),
                    makeCell(toYen(visit.amount), 'text-end'),
                    makeCell(paymentText || '-'),
                    makeCell(visit.memo),
                    makeCell(visit.statusDisplay || visit.status)
                );
                return row;
            },
            '伝票はありません。'
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
                    makeCell(cast.usesSendService ? 'あり' : '-'),
                    makeCell(toYen(cast.castSalesAmount), 'text-end'),
                    makeCell(toYen(cast.champagneBackAmount), 'text-end'),
                    makeCell(toYen(cast.advanceAmount), 'text-end')
                );
                return row;
            },
            '出勤キャストはいません。'
        );

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
            unavailable.has('expenses') ? '旧形式では未保存です。' : '当日支払の領収書支出はありません。'
        );

        root.querySelector('[data-report-memo]').textContent =
            businessDay.memo?.trim() || 'なし';
        const captured = report.capturedAt ? new Date(report.capturedAt) : null;
        root.querySelector('[data-report-captured]').textContent =
            captured && !Number.isNaN(captured.valueOf())
                ? `作成時刻 ${dateTimeFormatter.format(captured)}`
                : '';

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
    printButton.addEventListener('click', () => window.print());
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
