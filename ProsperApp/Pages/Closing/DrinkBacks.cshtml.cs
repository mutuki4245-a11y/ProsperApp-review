using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Closing;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public sealed class ClosingDrinkBacksModel(
    IFeatureGate featureGate,
    IDrinkBackRepository drinkBackRepository,
    ILocalSettingsProvider localSettingsProvider,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IDrinkBackRepository _drinkBackRepository = drinkBackRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IStoreClock _storeClock = storeClock;

    public long DepartmentId => _localSettingsProvider.GetCurrent().StoreDepartmentId;

    public DateOnly CurrentBusinessDate => _storeClock.GetCurrentBusinessDate();

    public IActionResult OnGet()
    {
        return _featureGate.IsEnabled(FeatureNames.Closing)
            ? RedirectToPage("/Closing/Index", new { modal = "drinkBack" })
            : NotFound();
    }

    public async Task<IActionResult> OnGetEditorAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var result = await _drinkBackRepository.GetCurrentEditorAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                message = result.ErrorMessage
            });
        }

        return new JsonResult(new
        {
            succeeded = true,
            result.Value.Editor,
            result.Value.CastMasterRevision,
            result.Value.ActiveCasts
        });
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var input = await Request.ReadFromJsonAsync<DrinkBackSaveMutation>(cancellationToken);
        if (input is null)
        {
            return BadRequest(new
            {
                status = "validation_error",
                message = "保存内容を確認してください。"
            });
        }

        var result = await _drinkBackRepository.SaveAsync(input, cancellationToken);
        if (!result.Succeeded)
        {
            var error = new
            {
                status = "unavailable",
                operationId = input.OperationId,
                message = result.ErrorMessage ?? "ドリンクバック調整を保存できませんでした。"
            };
            return result.FailureKind is ResultFailureKind.InvalidInput
                ? BadRequest(error)
                : StatusCode(StatusCodes.Status503ServiceUnavailable, error);
        }

        var output = result.Value;
        var payload = new
        {
            output.Status,
            output.OperationId,
            output.Message,
            output.Editor,
            output.DrinkBackStatus,
            output.DashboardDelta,
            output.BusinessDayRevision
        };
        return output.Status switch
        {
            "confirmed" => new JsonResult(payload),
            "conflict" => StatusCode(StatusCodes.Status409Conflict, payload),
            _ => BadRequest(payload)
        };
    }
}
