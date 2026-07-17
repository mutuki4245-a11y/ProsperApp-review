using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    [BindProperty]
    public string OrderQueueJson { get; set; } = string.Empty;

    [BindProperty]
    public string OrderQueueSummaryJson { get; set; } = string.Empty;

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
        return Page();
    }

    public async Task<IActionResult> OnGetSlipOptionsAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        var currentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (currentBusinessDay is null)
        {
            return new JsonResult(new { succeeded = true, slips = Array.Empty<object>() });
        }

        var slips = await _orderRepository.GetOpenSlipsAsync(currentBusinessDay.BusinessDayId, cancellationToken);
        return new JsonResult(new
        {
            succeeded = true,
            slips = slips.Select(slip => new
            {
                id = slip.SlipId,
                display = slip.TableDisplayName,
                openedTime = StoreBusinessTime.FormatStoreTime(slip.OpenedAt),
                customerCount = slip.CustomerCount,
                customerNames = slip.CustomerDisplayName,
                memo = slip.Memo
            })
        });
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        QueueLines = _orderQueueService.ReadPostedQueue(OrderQueueJson, QueueLines);
        await LoadOptionsAsync(cancellationToken);
        ValidateBusinessRules();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _orderRepository.AddOrderLinesAsync(0, QueueLines, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "注文を登録できませんでした。");
            return Page();
        }

        ModelState.Clear();
        var successMessage = BuildSuccessMessage(result);
        SelectedSlipId = null;
        QueueLines = [];
        OrderQueueSummaryJson = string.Empty;
        SuccessMessage = successMessage;
        return Page();
    }

    public StoreOrderSlipOption? SelectedSlip => SelectedSlipId is null
        ? null
        : Slips.FirstOrDefault(x => x.SlipId == SelectedSlipId.Value);

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var storeContextTask = _slipRepository.GetStoreContextAsync(cancellationToken);
        var currentBusinessDayTask = _businessDayRepository.GetCurrentAsync(cancellationToken);
        var itemsTask = _orderRepository.GetItemsAsync(cancellationToken);

        await Task.WhenAll(storeContextTask, currentBusinessDayTask, itemsTask);

        StoreContext = await storeContextTask;
        CurrentBusinessDay = await currentBusinessDayTask;

        if (CurrentBusinessDay is null)
        {
            Slips = [];
            Items = [];
            AttendanceCasts = [];
            return;
        }

        var attendanceCastsTask = _orderRepository.GetAttendanceCastsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);

        Slips = [];
        Items = await itemsTask;
        AttendanceCasts = await attendanceCastsTask;
    }

    private void ValidateBusinessRules()
    {
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "注文登録の前に営業中画面で伝票を作成してください。最初の伝票作成時に営業日を自動作成します。");
            return;
        }

        foreach (var error in _orderQueueService.ValidateOrderEntryQueue(QueueLines, Items, AttendanceCasts))
        {
            ModelState.AddModelError(nameof(QueueLines), error);
        }

        if (QueueLines.Any(x => x.SlipId is null or <= 0))
        {
            ModelState.AddModelError(nameof(QueueLines), "注文キューに利用できない卓番があります。");
        }
    }

    private string BuildSuccessMessage(AddStoreOrderLinesResult result)
    {
        var summaries = ReadPostedQueueSummaries()
            .Where(x => x.Count > 0)
            .GroupBy(x => x.SlipId)
            .Select(x =>
            {
                var first = x.First();
                var display = string.IsNullOrWhiteSpace(first.Display) ? $"伝票 {first.SlipId}" : first.Display.Trim();
                return $"{display}: {x.Sum(line => line.Count)}件";
            })
            .ToArray();

        if (summaries.Length == 0)
        {
            return $"注文を登録しました。登録行数: {result.InsertedCount}";
        }

        return $"注文を登録しました。登録行数: {result.InsertedCount}（{string.Join(" / ", summaries)}）";
    }

    private IReadOnlyList<OrderQueueSummaryInput> ReadPostedQueueSummaries()
    {
        if (string.IsNullOrWhiteSpace(OrderQueueSummaryJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<OrderQueueSummaryInput>>(
                OrderQueueSummaryJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record OrderQueueSummaryInput(
        [property: JsonPropertyName("slipId")] long SlipId,
        [property: JsonPropertyName("display")] string? Display,
        [property: JsonPropertyName("count")] int Count);
}
