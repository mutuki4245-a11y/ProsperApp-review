using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Services;

namespace ProsperApp.Features.Orders;

public sealed class OrderEntryApplicationService(
    IStoreOrderRepository orderRepository,
    IStoreSlipRepository slipRepository,
    IStoreClock storeClock,
    IStoreMasterBootstrapper masterBootstrapper) : IOrderEntryApplicationService
{
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<OrderEntryPageState> LoadPageAsync(CancellationToken ct)
    {
        await _masterBootstrapper.EnsureAsync(ct);
        var contextTask = _slipRepository.GetStoreContextAsync(ct);
        var itemsTask = _orderRepository.GetItemsAsync(ct);
        await Task.WhenAll(contextTask, itemsTask);

        var context = await contextTask;
        var items = await itemsTask;
        var issues = new List<PageLoadIssue>();
        AddIssue(issues, "店舗設定", context);
        AddIssue(issues, "商品", items);

        return new OrderEntryPageState(
            context.Succeeded ? context.Value : null,
            null,
            items.Succeeded ? items.Value : [],
            [],
            issues,
            issues.Count == 0
                ? _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow())
                : null);
    }

    public async Task<Result<IReadOnlyList<StoreOrderSlipOption>>> GetOpenSlipsAsync(CancellationToken ct)
    {
        var candidates = await _orderRepository.GetCurrentCandidatesAsync(ct);
        return candidates.Succeeded
            ? Result<IReadOnlyList<StoreOrderSlipOption>>.Success(candidates.Value.Slips)
            : Result<IReadOnlyList<StoreOrderSlipOption>>.Failure(
                candidates.FailureKind ?? ResultFailureKind.Unavailable,
                candidates.ErrorMessage ?? "注文対象の伝票を取得できませんでした。");
    }

    public Task<Result<OrderEntryCandidates>> GetCandidatesAsync(CancellationToken ct) =>
        _orderRepository.GetCurrentCandidatesAsync(ct);

    public Task<Result<OrderEntrySubmitResult>> SubmitAsync(OrderEntrySubmitInput input, CancellationToken ct) =>
        _orderRepository.SubmitCurrentAsync(input, ct);

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
