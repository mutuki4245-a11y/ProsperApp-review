using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Features.Orders;

public sealed class OrderEntryApplicationService(
    IBusinessDayRepository businessDayRepository,
    IStoreOrderRepository orderRepository,
    IStoreSlipRepository slipRepository,
    IOrderQueueService orderQueueService,
    IStoreClock storeClock) : IOrderEntryApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IOrderQueueService _orderQueueService = orderQueueService;
    private readonly IStoreClock _storeClock = storeClock;

    public async Task<OrderEntryPageState> LoadPageAsync(CancellationToken ct)
    {
        var contextTask = _slipRepository.GetStoreContextAsync(ct);
        var businessDayTask = _businessDayRepository.GetCurrentAsync(ct);
        var itemsTask = _orderRepository.GetItemsAsync(ct);
        await Task.WhenAll(contextTask, businessDayTask, itemsTask);

        var context = await contextTask;
        var businessDay = await businessDayTask;
        var items = await itemsTask;
        var issues = new List<PageLoadIssue>();
        AddIssue(issues, "店舗設定", context);
        AddIssue(issues, "営業日", businessDay);
        AddIssue(issues, "商品", items);

        Result<IReadOnlyList<StoreOrderAttendanceCastOption>> attendance =
            Result<IReadOnlyList<StoreOrderAttendanceCastOption>>.Success([]);
        if (businessDay.Succeeded && businessDay.Value is { } currentBusinessDay)
        {
            attendance = await _orderRepository.GetAttendanceCastsAsync(currentBusinessDay.BusinessDayId, ct);
            AddIssue(issues, "出勤キャスト", attendance);
        }

        return new OrderEntryPageState(
            context.Succeeded ? context.Value : null,
            businessDay.Succeeded ? businessDay.Value : null,
            businessDay.Succeeded && businessDay.Value is not null && items.Succeeded ? items.Value : [],
            attendance.Succeeded ? attendance.Value : [],
            issues,
            issues.Count == 0
                ? _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow())
                : null);
    }

    public async Task<Result<IReadOnlyList<StoreOrderSlipOption>>> GetOpenSlipsAsync(CancellationToken ct)
    {
        var businessDay = await _businessDayRepository.GetCurrentAsync(ct);
        if (!businessDay.Succeeded)
        {
            return Result<IReadOnlyList<StoreOrderSlipOption>>.Failure(
                businessDay.FailureKind ?? ResultFailureKind.Unavailable,
                businessDay.ErrorMessage ?? "現在営業日を取得できませんでした。");
        }

        return businessDay.Value is null
            ? Result<IReadOnlyList<StoreOrderSlipOption>>.Success([])
            : await _orderRepository.GetOpenSlipsAsync(businessDay.Value.BusinessDayId, ct);
    }

    public IReadOnlyList<OrderQueueInputModel> ReadPostedQueue(
        string? queueJson,
        IReadOnlyList<OrderQueueInputModel> fallbackLines) =>
        _orderQueueService.ReadPostedQueue(queueJson, fallbackLines);

    public IReadOnlyList<string> ValidateQueue(
        IReadOnlyList<OrderQueueInputModel> lines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts) =>
        _orderQueueService.ValidateOrderEntryQueue(lines, items, attendanceCasts);

    public Task<AddStoreOrderLinesResult> AddOrderLinesAsync(
        IReadOnlyList<OrderQueueInputModel> lines,
        CancellationToken ct) =>
        _orderRepository.AddOrderLinesAsync(0, lines, ct);

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
}
