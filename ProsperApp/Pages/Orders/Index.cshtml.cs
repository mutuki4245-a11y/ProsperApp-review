using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Orders;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages.Orders;

public class IndexModel(
    IFeatureGate featureGate,
    IOrderEntryApplicationService orderEntryApplicationService,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IOrderEntryApplicationService _orderEntryApplicationService = orderEntryApplicationService;
    private readonly IStoreClock _storeClock = storeClock;

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

    public bool ShouldDiscardStoredQueue { get; private set; }

    public IReadOnlyList<PageLoadIssue> LoadIssues { get; private set; } = [];

    public DateTimeOffset? LastUpdatedAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(long? slipId, CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        SelectedSlipId = slipId;
        await LoadOptionsAsync(cancellationToken);
        SuccessMessage = TempData["SuccessMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnGetSlipOptionsAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        var result = await _orderEntryApplicationService.GetOpenSlipsAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                message = result.ErrorMessage ?? "注文対象の伝票を取得できませんでした。"
            });
        }

        var slips = result.Value;
        return new JsonResult(new
        {
            succeeded = true,
            updatedAt = _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()),
            slips = slips.Select(slip => new
            {
                id = slip.SlipId,
                display = slip.TableDisplayName,
                openedTime = _storeClock.FormatBusinessTime(slip.OpenedAt),
                customerCount = slip.CustomerCount,
                customerNames = slip.CustomerDisplayName,
                nominationCastIds = slip.NominationCastIdList,
                nominationCastNames = slip.NominationCastDisplayName,
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

        QueueLines = _orderEntryApplicationService.ReadPostedQueue(OrderQueueJson, QueueLines).ToList();
        await LoadOptionsAsync(cancellationToken);
        ValidateBusinessRules();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _orderEntryApplicationService.AddOrderLinesAsync(QueueLines, cancellationToken);
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
        ShouldDiscardStoredQueue = true;
        return Page();
    }

    public StoreOrderSlipOption? SelectedSlip => SelectedSlipId is null
        ? null
        : Slips.FirstOrDefault(x => x.SlipId == SelectedSlipId.Value);

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var state = await _orderEntryApplicationService.LoadPageAsync(cancellationToken);
        StoreContext = state.StoreContext;
        CurrentBusinessDay = state.BusinessDay;
        Slips = [];
        Items = state.Items;
        AttendanceCasts = state.AttendanceCasts;
        LoadIssues = state.LoadIssues;
        LastUpdatedAt = state.LastUpdatedAt;
    }

    private void ValidateBusinessRules()
    {
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "注文登録の前に営業中画面で伝票を作成してください。最初の伝票作成時に営業日を自動作成します。");
            return;
        }

        if (LoadIssues.Count > 0)
        {
            ModelState.AddModelError(string.Empty, "必要な情報を取得できないため注文を登録できません。再取得してください。");
            return;
        }

        foreach (var error in _orderEntryApplicationService.ValidateQueue(QueueLines, Items, AttendanceCasts))
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
