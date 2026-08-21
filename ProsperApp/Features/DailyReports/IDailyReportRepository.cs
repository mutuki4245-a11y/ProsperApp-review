using ProsperApp.Features.Shared;

namespace ProsperApp.Features.DailyReports;

public interface IDailyReportRepository
{
    Task<Result<DailyReportDocument>> GetAsync(long? businessDayId, CancellationToken ct);
}
