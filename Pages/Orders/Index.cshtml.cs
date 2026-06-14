using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages.Orders;

public class IndexModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreOrderRepository orderRepository,
    IStoreSlipRepository slipRepository,
    IOrderQueueService orderQueueService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IOrderQueueService _orderQueueService = orderQueueService;

    [BindProperty(SupportsGet = true)]
    public long? SelectedSlipId { get; set; }

    [BindProperty]
    public List<OrderQueueInputModel> QueueLines { get; set; } = [];

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public IReadOnlyList<StoreOrderSlipOption> Slips { get; set; } = [];

    public IReadOnlyList<StoreOrderItemOption> Items { get; set; } = [];

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; set; } = [];

    public StoreContext? StoreContext { get; set; }

    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long? slipId, CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        SelectedSlipId = slipId;
        await LoadOptionsAsync(cancellationToken);
        if (SelectedSlipId is not null && Slips.All(x => x.SlipId != SelectedSlipId.Value))
        {
            SelectedSlipId = null;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        QueueLines = _orderQueueService.Normalize(QueueLines);
        await LoadOptionsAsync(cancellationToken);
        ValidateBusinessRules();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _orderRepository.AddOrderLinesAsync(SelectedSlipId!.Value, QueueLines, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "注文を登録できませんでした。");
            return Page();
        }

        ModelState.Clear();
        SelectedSlipId = null;
        QueueLines = [];
        await LoadOptionsAsync(cancellationToken);
        SuccessMessage = $"注文を登録しました。登録行数: {result.InsertedCount}";
        return Page();
    }

    public StoreOrderSlipOption? SelectedSlip => SelectedSlipId is null
        ? null
        : Slips.FirstOrDefault(x => x.SlipId == SelectedSlipId.Value);

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        StoreContext = await _slipRepository.GetStoreContextAsync(cancellationToken);
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        Slips = CurrentBusinessDay is null
            ? []
            : await _orderRepository.GetOpenSlipsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        Items = CurrentBusinessDay is null
            ? []
            : await _orderRepository.GetItemsAsync(cancellationToken);
        AttendanceCasts = CurrentBusinessDay is null
            ? []
            : await _orderRepository.GetAttendanceCastsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
    }

    private void ValidateBusinessRules()
    {
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業日が開始されていません。営業準備を実行してください。");
            return;
        }

        if (Slips.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "注文登録できるopen伝票がありません。");
        }

        if (SelectedSlipId is null || Slips.All(x => x.SlipId != SelectedSlipId.Value))
        {
            ModelState.AddModelError(nameof(SelectedSlipId), "卓番を選択してください。");
        }

        foreach (var error in _orderQueueService.Validate(
                     QueueLines,
                     Items,
                     AttendanceCasts,
                     requireAttendingCastForBackTarget: false,
                     missingItemsMessage: "商品マスタが未登録です。store_item_masterを確認してください。"))
        {
            ModelState.AddModelError(nameof(QueueLines), error);
        }
    }
}
