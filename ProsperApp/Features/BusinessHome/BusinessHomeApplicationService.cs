using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Features.BusinessHome;

public sealed class BusinessHomeApplicationService(
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreOrderRepository orderRepository,
    INominationBackAdminRepository nominationBackRepository,
    ICheckoutRepository checkoutRepository,
    IStoreClock storeClock) : IBusinessHomeApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly INominationBackAdminRepository _nominationBackRepository = nominationBackRepository;
    private readonly ICheckoutRepository _checkoutRepository = checkoutRepository;
    private readonly IStoreClock _storeClock = storeClock;

    public async Task<Result<BusinessHomeSnapshotState>> GetSnapshotAsync(CancellationToken ct)
    {
        var businessDayResult = await _businessDayRepository.GetCurrentAsync(ct);
        if (!businessDayResult.Succeeded)
        {
            return Result<BusinessHomeSnapshotState>.Failure(
                businessDayResult.FailureKind ?? ResultFailureKind.Unavailable,
                businessDayResult.ErrorMessage ?? "現在営業日を取得できませんでした。");
        }

        var businessDay = businessDayResult.Value;
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

    public async Task<BusinessHomePageState> LoadPageAsync(
        bool ordersEnabled,
        bool checkoutEnabled,
        bool includeAttendanceCasts,
        CancellationToken ct)
    {
        var storeContextTask = _slipRepository.GetStoreContextAsync(ct);
        var businessDayTask = _businessDayRepository.GetCurrentAsync(ct);
        var tablesTask = _slipRepository.GetTablesAsync(ct);
        var nominationsTask = _nominationBackRepository.GetSettingsAsync(ct);
        var itemsTask = ordersEnabled
            ? _orderRepository.GetItemsAsync(ct)
            : Task.FromResult(Result<IReadOnlyList<StoreOrderItemOption>>.Success([]));
        var paymentMethodsTask = checkoutEnabled
            ? _checkoutRepository.GetPaymentMethodsAsync(ct)
            : Task.FromResult(Result<IReadOnlyList<CheckoutPaymentMethod>>.Success([]));

        await Task.WhenAll(
            storeContextTask,
            businessDayTask,
            tablesTask,
            nominationsTask,
            itemsTask,
            paymentMethodsTask);

        var storeContext = await storeContextTask;
        var businessDay = await businessDayTask;
        var tables = await tablesTask;
        var nominations = await nominationsTask;
        var items = await itemsTask;
        var paymentMethods = await paymentMethodsTask;
        var issues = new List<PageLoadIssue>();
        AddIssue(issues, "店舗設定", storeContext);
        AddIssue(issues, "営業日", businessDay);
        AddIssue(issues, "卓番", tables);
        AddIssue(issues, "指名区分", nominations);
        AddIssue(issues, "商品", items);
        AddIssue(issues, "決済方法", paymentMethods);

        Result<IReadOnlyList<StoreOrderAttendanceCastOption>> attendance =
            Result<IReadOnlyList<StoreOrderAttendanceCastOption>>.Success([]);
        if (includeAttendanceCasts && businessDay.Succeeded && businessDay.Value is { } currentBusinessDay)
        {
            attendance = await _orderRepository.GetAttendanceCastsAsync(currentBusinessDay.BusinessDayId, ct);
            AddIssue(issues, "出勤キャスト", attendance);
        }

        return new BusinessHomePageState(
            storeContext.Succeeded ? storeContext.Value : null,
            businessDay.Succeeded ? businessDay.Value : null,
            _storeClock.GetCurrentBusinessDate(),
            tables.Succeeded ? tables.Value : [],
            nominations.Succeeded
                ? nominations.Value
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList()
                : [],
            items.Succeeded ? items.Value.Where(x => x.IsStandard).ToList() : [],
            attendance.Succeeded ? attendance.Value : [],
            paymentMethods.Succeeded ? paymentMethods.Value : [],
            issues,
            issues.Count == 0
                ? _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow())
                : null);
    }

    public async Task<Result<IReadOnlyList<StoreOrderAttendanceCastOption>>> GetAttendanceCastsAsync(
        CancellationToken ct)
    {
        var businessDay = await _businessDayRepository.GetCurrentAsync(ct);
        if (!businessDay.Succeeded)
        {
            return Result<IReadOnlyList<StoreOrderAttendanceCastOption>>.Failure(
                businessDay.FailureKind ?? ResultFailureKind.Unavailable,
                businessDay.ErrorMessage ?? "現在営業日を取得できませんでした。");
        }

        return businessDay.Value is null
            ? Result<IReadOnlyList<StoreOrderAttendanceCastOption>>.Success([])
            : await _orderRepository.GetAttendanceCastsAsync(businessDay.Value.BusinessDayId, ct);
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

        var businessDayResult = await _businessDayRepository.GetCurrentAsync(ct);
        if (!businessDayResult.Succeeded)
        {
            return Result<BusinessHomeFlushOutput>.Failure(
                businessDayResult.FailureKind ?? ResultFailureKind.Unavailable,
                businessDayResult.ErrorMessage ?? "現在営業日を取得できませんでした。");
        }

        var businessDay = businessDayResult.Value;
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

    public Task<CreateSlipResult> CreateSlipAsync(
        CreateSlipInputModel input,
        CancellationToken ct)
    {
        return _slipRepository.CreateSlipAsync(input, ct);
    }

    public Task<CheckoutStatementResult> IssueCheckoutStatementAsync(
        long slipId,
        DateTimeOffset closedAt,
        CancellationToken ct)
    {
        return _checkoutRepository.IssueCheckoutStatementAsync(slipId, closedAt, ct);
    }

    public Task<CheckoutStatementResult> GetCheckoutStatementPrintDataAsync(
        long slipId,
        CancellationToken ct)
    {
        return _checkoutRepository.GetCheckoutStatementPrintDataAsync(slipId, ct);
    }

    public Task<ReleaseCheckoutReadyResult> ReleaseCheckoutReadyAsync(
        long slipId,
        CancellationToken ct)
    {
        return _checkoutRepository.ReleaseCheckoutReadyAsync(slipId, ct);
    }

    public Task<ConfirmCheckoutResult> ConfirmCheckoutAsync(
        long slipId,
        IReadOnlyList<CheckoutPaymentInputModel> payments,
        decimal? receivedAmount,
        CancellationToken ct)
    {
        return _checkoutRepository.ConfirmCheckoutAsync(slipId, payments, receivedAmount, ct);
    }

    public Task<ReceiptPrintDataResult> GetCheckoutReceiptPrintDataAsync(
        long slipId,
        CancellationToken ct)
    {
        return _checkoutRepository.GetCheckoutReceiptPrintDataAsync(slipId, ct);
    }

    public Task<CancelCheckoutResult> CancelCheckoutAsync(
        long slipId,
        CancellationToken ct)
    {
        return _checkoutRepository.CancelCheckoutAsync(slipId, ct);
    }

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
