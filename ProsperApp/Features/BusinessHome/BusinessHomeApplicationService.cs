using ProsperApp.Features.Shared;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Features.BusinessHome;

public sealed class BusinessHomeApplicationService(
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreClock storeClock) : IBusinessHomeApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreClock _storeClock = storeClock;

    public async Task<Result<BusinessHomeSnapshotState>> GetSnapshotAsync(CancellationToken ct)
    {
        var businessDay = await _businessDayRepository.GetCurrentAsync(ct);
        var businessDate = businessDay?.BusinessDate ?? _storeClock.GetCurrentBusinessDate();
        if (businessDay is null)
        {
            return Result<BusinessHomeSnapshotState>.Success(
                new BusinessHomeSnapshotState(null, businessDate, null));
        }

        var result = await _slipRepository.GetBusinessDaySnapshotAsync(businessDay.BusinessDayId, ct);
        return result.Succeeded
            ? Result<BusinessHomeSnapshotState>.Success(
                new BusinessHomeSnapshotState(businessDay, businessDate, result.Snapshot))
            : Result<BusinessHomeSnapshotState>.Failure(
                ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "営業中の伝票を取得できませんでした。");
    }

    public async Task<Result<BusinessHomeFlushOutput>> FlushAsync(
        BusinessHomeChangeFlushInput input,
        CancellationToken ct)
    {
        var validation = Validate(input);
        if (!validation.Succeeded)
        {
            return Result<BusinessHomeFlushOutput>.Failure(
                validation.FailureKind ?? ResultFailureKind.InvalidInput,
                validation.ErrorMessage ?? "保存内容を確認してください。");
        }

        var businessDay = await _businessDayRepository.GetCurrentAsync(ct);
        if (businessDay is null)
        {
            return Result<BusinessHomeFlushOutput>.Failure(
                ResultFailureKind.Conflict,
                "営業中の営業日がありません。");
        }

        var result = await _slipRepository.FlushBusinessHomeChangesAsync(
            input,
            businessDay.BusinessDayId,
            ct);
        return result.Succeeded
            ? Result<BusinessHomeFlushOutput>.Success(
                new BusinessHomeFlushOutput(
                    input.BatchId,
                    result.Snapshot,
                    result.OperationResults,
                    result.KaraokeResults))
            : Result<BusinessHomeFlushOutput>.Failure(
                ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "営業中の変更を保存できませんでした。");
    }

    private static Result<BusinessHomeChangeFlushInput> Validate(BusinessHomeChangeFlushInput input)
    {
        if (input is null ||
            input.Operations is null ||
            input.KaraokeLines is null ||
            string.IsNullOrWhiteSpace(input.BatchId) ||
            input.BatchId.Length > 100 ||
            input.Operations.Count > 100 ||
            input.KaraokeLines.Count > 100 ||
            input.Operations.Any(operation => !BusinessHomeOperationParser.Parse(operation).Succeeded) ||
            input.KaraokeLines.Any(line =>
                line.SlipId <= 0 ||
                string.IsNullOrWhiteSpace(line.DraftId) ||
                line.DraftId.Length > 100 ||
                line.Quantity < 0 ||
                line.Quantity > 999 ||
                line.Quantity != decimal.Truncate(line.Quantity)))
        {
            return Result<BusinessHomeChangeFlushInput>.Failure(
                ResultFailureKind.InvalidInput,
                "保存内容を確認してください。");
        }

        return Result<BusinessHomeChangeFlushInput>.Success(input);
    }
}
