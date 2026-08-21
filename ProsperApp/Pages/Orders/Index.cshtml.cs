using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
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

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public IReadOnlyList<StoreOrderSlipOption> Slips { get; set; } = [];

    public IReadOnlyList<StoreOrderItemOption> Items { get; set; } = [];

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; set; } = [];

    public StoreContext? StoreContext { get; set; }

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
        return Page();
    }

    public async Task<IActionResult> OnGetSlipOptionsAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        var result = await _orderEntryApplicationService.GetCandidatesAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                message = result.ErrorMessage ?? "注文対象の伝票を取得できませんでした。"
            });
        }

        var candidates = result.Value;
        var slips = candidates.Slips;
        return new JsonResult(new
        {
            succeeded = true,
            updatedAt = _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()),
            businessDay = candidates.BusinessDay,
            revision = candidates.Revision,
            attendanceCasts = candidates.AttendanceCasts.Select(cast => new
            {
                id = cast.CastId,
                name = cast.DisplayName,
                display = cast.SearchDisplayName,
                drinkMemo = cast.DrinkMemo,
                department = cast.DepartmentName
            }),
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

    public async Task<IActionResult> OnPostSubmitV2Async(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        OrderEntrySubmitInput? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<OrderEntrySubmitInput>(
                Request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null)
        {
            return BadRequest(new { succeeded = false, status = "validation_error", message = "注文内容を確認してください。" });
        }

        var result = await _orderEntryApplicationService.SubmitAsync(input, cancellationToken);
        if (!result.Succeeded)
        {
            var status = result.FailureKind == ResultFailureKind.Conflict
                ? "conflict"
                : result.FailureKind == ResultFailureKind.InvalidInput
                    ? "validation_error"
                    : "unavailable";
            return StatusCode(
                status == "conflict" ? StatusCodes.Status409Conflict :
                status == "validation_error" ? StatusCodes.Status400BadRequest :
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    succeeded = false,
                    status,
                    operationId = input.OperationId,
                    message = result.ErrorMessage ?? "注文を登録できませんでした。"
                });
        }

        var output = result.Value;
        var payload = new
        {
            succeeded = output.Status == "confirmed",
            status = output.Status,
            operationId = output.OperationId,
            insertedCount = output.InsertedCount,
            insertedLines = output.InsertedLines,
            businessDayId = output.BusinessDayId,
            businessDayRevision = output.BusinessDayRevision,
            message = output.Message,
            recoveryCandidates = output.RecoveryCandidates
        };
        return output.Status switch
        {
            "confirmed" => new JsonResult(payload),
            "conflict" => StatusCode(StatusCodes.Status409Conflict, payload),
            _ => BadRequest(payload)
        };
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

}
