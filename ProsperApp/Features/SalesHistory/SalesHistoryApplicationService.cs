using ProsperApp.Features.Shared;

namespace ProsperApp.Features.SalesHistory;

public sealed class SalesHistoryApplicationService(
    ISalesHistoryRepository repository) : ISalesHistoryApplicationService
{
    private const decimal MaximumAmount = 999_999_999_999m;
    private readonly ISalesHistoryRepository _repository = repository;

    public Task<Result<SalesHistoryPage>> GetPageAsync(
        SalesHistoryQuery query,
        DateOnly currentBusinessDate,
        CancellationToken ct)
    {
        if (query.FromBusinessDate > query.ToBusinessDate ||
            query.PageSize is < 1 or > 100 ||
            (query.BeforeBusinessDayId is <= 0))
        {
            return Task.FromResult(Result<SalesHistoryPage>.Failure(
                ResultFailureKind.InvalidInput,
                "営業履歴の検索条件が正しくありません。"));
        }

        return _repository.GetPageAsync(query, currentBusinessDate, ct);
    }

    public Task<Result<SalesHistoryCorrectionEditor>> GetCorrectionEditorAsync(
        long businessDayId,
        DateOnly currentBusinessDate,
        CancellationToken ct)
    {
        if (businessDayId <= 0)
        {
            return Task.FromResult(Result<SalesHistoryCorrectionEditor>.Failure(
                ResultFailureKind.InvalidInput,
                "営業日を確認してください。"));
        }

        return _repository.GetCorrectionEditorAsync(businessDayId, currentBusinessDate, ct);
    }

    public Task<Result<SalesHistoryCorrectionResult>> SaveCorrectionAsync(
        SalesHistoryCorrectionMutation mutation,
        CancellationToken ct)
    {
        var reason = mutation.Reason?.Trim();
        if (!Guid.TryParse(mutation.OperationId, out _) ||
            mutation.BusinessDayId <= 0 ||
            mutation.ExpectedSnapshotVersion <= 0 ||
            mutation.CurrentBusinessDate == default ||
            string.IsNullOrWhiteSpace(mutation.ActorEmail) ||
            string.IsNullOrWhiteSpace(reason) ||
            reason.Length > 500 ||
            mutation.Memo?.Length > 500 ||
            !IsWholeAmount(mutation.DrinkDeliveryAmount, allowNegative: false) ||
            mutation.CastEntries is null ||
            mutation.StaffEntries is null ||
            mutation.DrinkBackAdjustments is null ||
            mutation.CastSalesSlips is null)
        {
            return Task.FromResult(Result<SalesHistoryCorrectionResult>.Failure(
                ResultFailureKind.InvalidInput,
                "補正内容を確認してください。"));
        }

        if (mutation.DrinkBackAdjustments.Any(x =>
                x.CastId <= 0 ||
                (x.IsActive && (!x.AdjustmentAmount.HasValue || !IsWholeAmount(x.AdjustmentAmount.Value, allowNegative: true)))) ||
            mutation.CastSalesSlips.Any(slip =>
                slip.SlipId <= 0 ||
                slip.SourceAmountType is not ("subtotal" or "total") ||
                slip.SplitMode is not ("split" or "full") ||
                slip.Adjustments is null ||
                slip.Adjustments.Any(x => x.SlipCastId <= 0 ||
                    (x.SalesAmount.HasValue && !IsWholeAmount(x.SalesAmount.Value, allowNegative: false)))))
        {
            return Task.FromResult(Result<SalesHistoryCorrectionResult>.Failure(
                ResultFailureKind.InvalidInput,
                "金額または売上配分を確認してください。"));
        }

        return _repository.SaveCorrectionAsync(mutation with { Reason = reason }, ct);
    }

    private static bool IsWholeAmount(decimal value, bool allowNegative)
    {
        return decimal.Truncate(value) == value &&
               (allowNegative ? decimal.Abs(value) <= MaximumAmount : value is >= 0 and <= MaximumAmount);
    }
}
