using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Features.Closing;

public sealed class ClosingApplicationService(
    IBusinessDayRepository businessDayRepository,
    IStoreClock storeClock) : IClosingApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreClock _storeClock = storeClock;

    public async Task<Result<ClosingPageState>> LoadAsync(
        bool includeReadiness,
        bool forceRefresh,
        CancellationToken ct)
    {
        var businessDay = await _businessDayRepository.GetCurrentAsync(ct, forceRefresh);
        if (!businessDay.Succeeded)
        {
            return Result<ClosingPageState>.Failure(
                businessDay.FailureKind ?? ResultFailureKind.Unavailable,
                businessDay.ErrorMessage ?? "現在営業日を取得できませんでした。");
        }

        if (businessDay.Value is null || !includeReadiness)
        {
            return Result<ClosingPageState>.Success(new ClosingPageState(
                businessDay.Value,
                null,
                _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow())));
        }

        var readiness = await _businessDayRepository.GetClosingReadinessAsync(
            businessDay.Value,
            ct);
        if (!readiness.Succeeded)
        {
            return Result<ClosingPageState>.Failure(
                readiness.FailureKind ?? ResultFailureKind.Unavailable,
                readiness.ErrorMessage ?? "締め条件を取得できませんでした。");
        }

        return Result<ClosingPageState>.Success(new ClosingPageState(
            businessDay.Value,
            readiness.Value,
            readiness.Value.CheckedAt ??
            _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow())));
    }

    public async Task<Result<StoreBusinessDay>> CloseAsync(
        long? submittedBusinessDayId,
        string? memo,
        bool ignoreClosingRequirements,
        CancellationToken ct)
    {
        var closeResult = await _businessDayRepository.CloseCurrentAsync(
            submittedBusinessDayId,
            memo,
            ignoreClosingRequirements,
            ct);
        return closeResult.Succeeded && closeResult.BusinessDay is not null
            ? Result<StoreBusinessDay>.Success(closeResult.BusinessDay)
            : Result<StoreBusinessDay>.Failure(
                ResultFailureKind.Unavailable,
                closeResult.ErrorMessage ?? "営業日を締められませんでした。");
    }
}
