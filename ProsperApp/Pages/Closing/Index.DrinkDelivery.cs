using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ProsperApp.Pages;

public partial class ClosingModel
{
    public async Task<IActionResult> OnGetDrinkDeliveryEditorAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var result = await _businessDayRepository.GetCurrentDrinkDeliveryEditorAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                errorMessage = result.ErrorMessage
            });
        }

        return new JsonResult(new { succeeded = true, editor = result.Value });
    }

    public async Task<IActionResult> OnPostDrinkDeliverySaveV2Async(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        DrinkDeliverySaveRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<DrinkDeliverySaveRequest>(
                Request.Body,
                RequestJsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null || !Guid.TryParse(request.OperationId, out var operationId) ||
            request.DrinkDeliveryAmount < 0 || request.DrinkDeliveryAmount > 999999999999m ||
            decimal.Truncate(request.DrinkDeliveryAmount) != request.DrinkDeliveryAmount)
        {
            return BadRequest(new
            {
                succeeded = false,
                status = "validation_error",
                operationId = request?.OperationId,
                message = "納品額は0円以上の整数で入力してください。"
            });
        }

        var result = await _businessDayRepository.SaveCurrentDrinkDeliveryAmountAsync(
            new CurrentBusinessDayDrinkDeliveryMutation(
                operationId.ToString("D"),
                request.ExpectedBusinessDayId,
                request.ExpectedBusinessDayRevision,
                _storeClock.GetCurrentBusinessDate(),
                request.DrinkDeliveryAmount),
            cancellationToken);
        if (!result.Succeeded)
        {
            var status = result.FailureKind switch
            {
                ResultFailureKind.Conflict => "conflict",
                ResultFailureKind.InvalidInput => "validation_error",
                _ => "unavailable"
            };
            return StatusCode(
                status == "conflict" ? StatusCodes.Status409Conflict :
                status == "validation_error" ? StatusCodes.Status400BadRequest :
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    succeeded = false,
                    status,
                    operationId = operationId.ToString("D"),
                    message = result.ErrorMessage ?? "納品額を保存できませんでした。"
                });
        }

        var output = result.Value;
        var payload = new
        {
            succeeded = output.Status == "confirmed",
            output.Status,
            output.OperationId,
            output.Message,
            output.Editor,
            output.DashboardDelta
        };
        return output.Status switch
        {
            "confirmed" => new JsonResult(payload),
            "conflict" => StatusCode(StatusCodes.Status409Conflict, payload),
            _ => BadRequest(payload)
        };
    }

    public sealed record DrinkDeliverySaveRequest(
        string OperationId,
        long? ExpectedBusinessDayId,
        long? ExpectedBusinessDayRevision,
        decimal DrinkDeliveryAmount);
}
