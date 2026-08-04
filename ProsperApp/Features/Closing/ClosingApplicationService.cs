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
        var businessDayResult = await _businessDayRepository.GetCurrentAsync(ct, forceRefresh: true);
        if (!businessDayResult.Succeeded)
        {
            return Result<StoreBusinessDay>.Failure(
                businessDayResult.FailureKind ?? ResultFailureKind.Unavailable,
                businessDayResult.ErrorMessage ?? "現在営業日を取得できませんでした。");
        }

        var businessDay = businessDayResult.Value;
        if (businessDay is null)
        {
            return Result<StoreBusinessDay>.Failure(
                ResultFailureKind.Conflict,
                "営業中の営業日がありません。");
        }

        if (submittedBusinessDayId != businessDay.BusinessDayId)
        {
            return Result<StoreBusinessDay>.Failure(
                ResultFailureKind.Conflict,
                "営業日情報が更新されています。画面を再読み込みしてください。");
        }

        if (!ignoreClosingRequirements)
        {
            var readinessResult = await _businessDayRepository.GetClosingReadinessAsync(
                businessDay,
                ct);
            if (!readinessResult.Succeeded)
            {
                return Result<StoreBusinessDay>.Failure(
                    readinessResult.FailureKind ?? ResultFailureKind.Unavailable,
                    readinessResult.ErrorMessage ?? "締め条件を取得できませんでした。");
            }

            var readiness = readinessResult.Value;
            if (!readiness.CanClose)
            {
                return Result<StoreBusinessDay>.Failure(
                    ResultFailureKind.Conflict,
                    string.Join(Environment.NewLine, readiness.BlockReasons));
            }
        }

        var closeResult = await _businessDayRepository.CloseAsync(
            businessDay.BusinessDayId,
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
