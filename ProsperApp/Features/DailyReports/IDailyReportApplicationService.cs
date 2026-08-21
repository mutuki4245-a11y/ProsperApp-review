using ProsperApp.Features.Shared;

namespace ProsperApp.Features.DailyReports;

public interface IDailyReportApplicationService
{
    Task<Result<DailyReportDocument>> LoadAsync(long? businessDayId, CancellationToken ct);
}
