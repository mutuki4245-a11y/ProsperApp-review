namespace ProsperApp.Features.DailyReports;

public sealed class DailyReportDocument
{
    public string SchemaVersion { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }

    public IReadOnlyList<string> LegacyUnavailableSections { get; init; } = [];

    public DailyReportBusinessDay BusinessDay { get; init; } = new();

    public DailyReportTotals Totals { get; init; } = new();

    public IReadOnlyList<DailyReportPayment> Payments { get; init; } = [];

    public IReadOnlyList<DailyReportItemCategory> ItemCategories { get; init; } = [];

    public IReadOnlyList<DailyReportVisit> Visits { get; init; } = [];

    public IReadOnlyList<DailyReportCast> Casts { get; init; } = [];

    public IReadOnlyList<DailyReportExpenseAccount> ExpenseAccounts { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class DailyReportBusinessDay
{
    public long BusinessDayId { get; init; }

    public DateOnly BusinessDate { get; init; }

    public string? DepartmentName { get; init; }

    public DateTimeOffset? OpenedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public string? Memo { get; init; }
}

public sealed class DailyReportTotals
{
    public decimal SalesAmount { get; init; }

    public decimal CashAmount { get; init; }

    public decimal? ExpenseAmount { get; init; }

    public decimal? CashBalanceAmount { get; init; }

    public decimal DrinkDeliveryAmount { get; init; }

    public int ConfirmedCheckoutCount { get; init; }

    public int SlipCount { get; init; }

    public int CustomerCount { get; init; }

    public int OpenSlipCount { get; init; }

    public decimal PaymentDifferenceAmount { get; init; }
}

public sealed class DailyReportPayment
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

public sealed class DailyReportItemCategory
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public decimal Amount { get; init; }
}

public sealed class DailyReportVisit
{
    public long SlipId { get; init; }

    public string? SlipNo { get; init; }

    public string? EntryTime { get; init; }

    public string? TableDisplay { get; init; }

    public int CustomerCount { get; init; }

    public string? CustomerNames { get; init; }

    public decimal Amount { get; init; }

    public IReadOnlyList<DailyReportPayment> Payments { get; init; } = [];

    public string? Memo { get; init; }

    public string? Status { get; init; }

    public string? StatusDisplay { get; init; }
}

public sealed class DailyReportCast
{
    public long CastId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public DateTimeOffset? ClockInAt { get; init; }

    public DateTimeOffset? ClockOutAt { get; init; }

    public bool UsesSendService { get; init; }

    public decimal CastSalesAmount { get; init; }

    public decimal ChampagneBackAmount { get; init; }

    public decimal? AdvanceAmount { get; init; }
}

public sealed class DailyReportExpenseAccount
{
    public string AccountCode { get; init; } = string.Empty;

    public string AccountName { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}
