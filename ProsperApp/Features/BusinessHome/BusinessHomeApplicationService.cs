using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Features.BusinessHome;

public sealed class BusinessHomeApplicationService(
    IStoreSlipRepository slipRepository,
    IStoreOrderRepository orderRepository,
    ICheckoutRepository checkoutRepository,
    IStoreClock storeClock) : IBusinessHomeApplicationService
{
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly ICheckoutRepository _checkoutRepository = checkoutRepository;
    private readonly IStoreClock _storeClock = storeClock;

    public async Task<Result<BusinessHomeSnapshotState>> GetSnapshotAsync(long? knownRevision, CancellationToken ct)
    {
        var result = await _slipRepository.GetCurrentBusinessHomeSnapshotAsync(knownRevision, ct);
        return result.Succeeded
            ? Result<BusinessHomeSnapshotState>.Success(
                new BusinessHomeSnapshotState(
                    result.Value.BusinessDay,
                    result.Value.BusinessDate,
                    result.Value.Revision,
                    result.Value.Unchanged,
                    result.Value.AttendanceCasts,
                    result.Value.Snapshot))
            : Result<BusinessHomeSnapshotState>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "営業中の伝票を取得できませんでした。");
    }

    public async Task<BusinessHomePageState> LoadPageAsync(
        bool ordersEnabled,
        bool checkoutEnabled,
        bool includeAttendanceCasts,
        CancellationToken ct)
    {
        var issues = new List<PageLoadIssue>();

        var bootstrap = await _slipRepository.GetBusinessHomeBootstrapAsync(ct);
        if (!bootstrap.Succeeded)
        {
            issues.Add(new PageLoadIssue(
                "初期データ",
                ResultFailureKind.Unavailable,
                bootstrap.ErrorMessage ?? "営業中トップの初期データを取得できませんでした。"));
        }

        if (bootstrap.Succeeded && bootstrap.StoreContext is null)
        {
            issues.Add(new PageLoadIssue(
                "店舗設定",
                ResultFailureKind.InvalidResponse,
                "店舗設定の応答形式が正しくありません。"));
        }

        if (bootstrap.Succeeded && bootstrap.BusinessDay is not null && bootstrap.Snapshot is null)
        {
            issues.Add(new PageLoadIssue(
                "営業中一覧",
                ResultFailureKind.InvalidResponse,
                "営業中一覧の初期データを取得できませんでした。"));
        }

        IReadOnlyList<NominationBackMasterItem> nominations = bootstrap.Succeeded
            ? bootstrap.NominationOptions
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .ToList()
            : [];
        IReadOnlyList<StoreOrderItemOption> items = bootstrap.Succeeded && ordersEnabled
            ? bootstrap.OrderItems.Where(x => x.IsStandard).ToList()
            : [];
        IReadOnlyList<StoreOrderAttendanceCastOption> attendance = bootstrap.Succeeded && includeAttendanceCasts
            ? bootstrap.AttendanceCasts
            : [];
        IReadOnlyList<CheckoutPaymentMethod> paymentMethods = bootstrap.Succeeded && checkoutEnabled
            ? bootstrap.PaymentMethods
            : [];

        return new BusinessHomePageState(
            bootstrap.Succeeded ? bootstrap.StoreContext : null,
            bootstrap.Succeeded ? bootstrap.BusinessDay : null,
            _storeClock.GetCurrentBusinessDate(),
            bootstrap.Succeeded ? bootstrap.Tables : [],
            nominations,
            items,
            attendance,
            paymentMethods,
            bootstrap.Succeeded ? bootstrap.Snapshot : null,
            issues,
            issues.Count == 0
                ? _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow())
                : null);
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

        var result = await _slipRepository.FlushBusinessHomeChangesAsync(
            input,
            ct);
        return result.Succeeded
            ? Result<BusinessHomeFlushOutput>.Success(
                new BusinessHomeFlushOutput(
                    input.BatchId,
                    result.Status,
                    result.BusinessDay,
                    result.BusinessDayRevision,
                    result.Snapshot,
                    result.OperationResults,
                    result.KaraokeResults))
            : Result<BusinessHomeFlushOutput>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "営業中の変更を保存できませんでした。");
    }

    public Task<Result<CheckoutMutationResult>> IssueCheckoutStatementV2Async(
        IssueCheckoutStatementV2Mutation mutation,
        CancellationToken ct) =>
        _checkoutRepository.IssueCheckoutStatementV2Async(mutation, ct);

    public Task<CheckoutStatementResult> GetCheckoutStatementPrintDataAsync(
        long slipId,
        CancellationToken ct)
    {
        return _checkoutRepository.GetCheckoutStatementPrintDataAsync(slipId, ct);
    }

    public Task<Result<CheckoutMutationResult>> ReleaseCheckoutReadyV2Async(
        ReleaseCheckoutReadyV2Mutation mutation,
        CancellationToken ct) =>
        _checkoutRepository.ReleaseCheckoutReadyV2Async(mutation, ct);

    public Task<Result<CheckoutMutationResult>> ConfirmCheckoutV2Async(
        ConfirmCheckoutV2Mutation mutation,
        CancellationToken ct) =>
        _checkoutRepository.ConfirmCheckoutV2Async(mutation, ct);

    public Task<ReceiptPrintDataResult> GetCheckoutReceiptPrintDataAsync(
        long slipId,
        CancellationToken ct)
    {
        return _checkoutRepository.GetCheckoutReceiptPrintDataAsync(slipId, ct);
    }

    public Task<Result<CheckoutMutationResult>> CancelCheckoutV2Async(
        CancelCheckoutV2Mutation mutation,
        CancellationToken ct) =>
        _checkoutRepository.CancelCheckoutV2Async(mutation, ct);

    private static void AddIssue<T>(
        ICollection<PageLoadIssue> issues,
        string area,
        Result<T> result)
    {
        if (!result.Succeeded)
        {
            issues.Add(new PageLoadIssue(
                area,
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? $"{area}を取得できませんでした。"));
        }
    }

    private static Result<BusinessHomeChangeFlushInput> Validate(BusinessHomeChangeFlushInput input)
    {
        if (input is null ||
            input.Operations is null ||
            input.KaraokeLines is null ||
            !Guid.TryParse(input.BatchId, out _) ||
            input.Operations.Count > 100 ||
            input.KaraokeLines.Count > 100 ||
            input.Operations.Any(operation => !BusinessHomeOperationParser.Parse(operation).Succeeded) ||
            input.KaraokeLines.Any(line =>
                line.SlipId <= 0 ||
                !Guid.TryParse(line.DraftId, out _) ||
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
