namespace ProsperApp.Features.DailyReports;

public sealed class DailyReportPartialViewModel
{
    public required string ReportUrl { get; init; }

    public string TitleId { get; init; } = "dailyReportTitle";

    public bool AutoLoad { get; init; } = true;
}
