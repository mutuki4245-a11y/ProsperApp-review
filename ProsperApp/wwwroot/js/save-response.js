(() => {
    const confirmedStatus = 'confirmed';
    const rejectedStatuses = new Set([
        'conflict',
        'validation_error',
        'permission_denied',
        'stale_work_item'
    ]);
    const terminalStatuses = new Set([confirmedStatus, ...rejectedStatuses]);

    const normalizeStatus = (value) => String(value ?? '').trim().toLowerCase();
    const statusOf = (value) => normalizeStatus(
        typeof value === 'string' ? value : value?.status ?? value?.Status);

    const isConfirmedStatus = (value) => statusOf(value) === confirmedStatus;
    const isRejectedStatus = (value) => rejectedStatuses.has(statusOf(value));
    const isTerminalStatus = (value) => terminalStatuses.has(statusOf(value));

    const unknown = (status = 'unavailable') => ({
        kind: 'unknown',
        status: status || 'unavailable',
        terminal: false,
        retry: true,
        rejected: false,
        confirmed: false
    });

    const confirmed = (status = confirmedStatus) => ({
        kind: 'confirmed',
        status,
        terminal: true,
        retry: false,
        rejected: false,
        confirmed: true
    });

    const rejected = (status) => ({
        kind: 'rejected',
        status,
        terminal: true,
        retry: false,
        rejected: true,
        confirmed: false
    });

    const classify = ({ response, payload, parseFailed = false, missingResult = false } = {}) => {
        const status = statusOf(payload);
        if (status === confirmedStatus) {
            return confirmed(status);
        }
        if (rejectedStatuses.has(status)) {
            return rejected(status);
        }
        if (parseFailed || missingResult || !payload || !response || response.status >= 500 ||
            status === '' || status === 'unavailable') {
            return unknown(status || 'unavailable');
        }
        if (response.ok === false && response.status < 500) {
            return rejected(status || 'validation_error');
        }

        return unknown(status || 'unavailable');
    };

    const classifyRow = (row) => {
        const status = statusOf(row);
        if (row?.succeeded === true || status === confirmedStatus) {
            return confirmed(status || confirmedStatus);
        }
        if (rejectedStatuses.has(status)) {
            return rejected(status);
        }

        return unknown(status || 'unavailable');
    };

    window.ProsperSaveResponse = Object.freeze({
        confirmedStatus,
        rejectedStatuses,
        terminalStatuses,
        normalizeStatus,
        statusOf,
        isConfirmedStatus,
        isRejectedStatus,
        isTerminalStatus,
        classify,
        classifyRow
    });
})();
