using ProsperApp.Features.Shared;

namespace ProsperApp.Features.DailyReports;

public sealed class DailyReportApplicationService(
    IDailyReportRepository repository) : IDailyReportApplicationService
{
    private readonly IDailyReportRepository _repository = repository;

    public Task<Result<DailyReportDocument>> LoadAsync(long? businessDayId, CancellationToken ct)
    {
        if (businessDayId is <= 0)
        {
            return Task.FromResult(Result<DailyReportDocument>.Failure(
                ResultFailureKind.InvalidInput,
                "日報の営業日情報が正しくありません。"));
        }

        return _repository.GetAsync(businessDayId, ct);
    }
}
