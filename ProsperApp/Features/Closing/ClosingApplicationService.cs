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
        bool includePendingReceipts,
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
            includePendingReceipts,
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
        bool includePendingReceipts,
        CancellationToken ct)
    {
        var state = await LoadAsync(
            includeReadiness: true,
            includePendingReceipts,
            forceRefresh: true,
            ct);
        if (!state.Succeeded)
        {
            return Result<StoreBusinessDay>.Failure(
                state.FailureKind ?? ResultFailureKind.Unavailable,
                state.ErrorMessage ?? "締め条件を取得できませんでした。");
        }

        var businessDay = state.Value.BusinessDay;
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

        var readiness = state.Value.Readiness;
        if (readiness is null)
        {
            return Result<StoreBusinessDay>.Failure(
                ResultFailureKind.InvalidResponse,
                "締め条件を取得できませんでした。");
        }

        if (!readiness.CanClose)
        {
            return Result<StoreBusinessDay>.Failure(
                ResultFailureKind.Conflict,
                string.Join(Environment.NewLine, readiness.BlockReasons));
        }

        var closeResult = await _businessDayRepository.CloseAsync(
            businessDay.BusinessDayId,
            memo,
            includePendingReceipts,
            ct);
        return closeResult.Succeeded && closeResult.BusinessDay is not null
            ? Result<StoreBusinessDay>.Success(closeResult.BusinessDay)
            : Result<StoreBusinessDay>.Failure(
                ResultFailureKind.Unavailable,
                closeResult.ErrorMessage ?? "営業日を締められませんでした。");
    }
}
