using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ProsperApp.Pages;

public partial class ClosingModel
{
    public async Task<IActionResult> OnGetCastSalesOverviewAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var result = await _castSalesAdjustmentRepository.GetCurrentOverviewAsync(cancellationToken);
        return result.Succeeded
            ? new JsonResult(new { succeeded = true, overview = result.Value })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                message = result.ErrorMessage ?? "キャスト売上額調整を取得できませんでした。"
            });
    }

    public async Task<IActionResult> OnPostCastSalesSaveV2Async(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var request = await ReadRequestAsync<CastSalesSaveRequest>(cancellationToken);
        var operationId = string.Empty;
        var message = "入力内容が正しくありません。";
        if (request is null || !TryValidateCastSalesSaveRequest(request, out operationId, out message))
        {
            return BadRequest(CastSalesValidationPayload(request?.OperationId, message));
        }

        var result = await _castSalesAdjustmentRepository.SaveCurrentAsync(
            new SaveCurrentCastSalesAdjustmentMutation(
                operationId,
                request.ExpectedBusinessDayId,
                request.ExpectedBusinessDayRevision,
                request.SlipId,
                request.ExpectedSlipVersion,
                request.ExpectedCheckoutId,
                request.ExpectedCheckoutVersion,
                request.SourceAmountType,
                request.SplitMode,
                request.Adjustments!.Select(value => new CastSalesAdjustmentValue(
                    value.SlipCastId,
                    value.SalesAmount)).ToArray()),
            cancellationToken);
        return CastSalesMutationResponse(result, operationId);
    }

    public async Task<IActionResult> OnPostCastSalesConfirmAllV2Async(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var request = await ReadRequestAsync<CastSalesConfirmAllRequest>(cancellationToken);
        var operationId = string.Empty;
        var message = "入力内容が正しくありません。";
        if (request is null || !TryValidateCastSalesConfirmRequest(request, out operationId, out message))
        {
            return BadRequest(CastSalesValidationPayload(request?.OperationId, message));
        }

        var result = await _castSalesAdjustmentRepository.ConfirmCurrentAsync(
            new ConfirmCurrentCastSalesAdjustmentsMutation(
                operationId,
                request.ExpectedBusinessDayId,
                request.ExpectedBusinessDayRevision,
                request.Slips!.Select(slip => new ConfirmCurrentCastSalesAdjustmentSlip(
                    slip.SlipId,
                    slip.ExpectedSlipVersion,
                    slip.ExpectedCheckoutId,
                    slip.ExpectedCheckoutVersion,
                    slip.SourceAmountType,
                    slip.SplitMode,
                    slip.Adjustments!.Select(value => new CastSalesAdjustmentValue(
                        value.SlipCastId,
                        value.SalesAmount)).ToArray())).ToArray()),
            cancellationToken);
        return CastSalesMutationResponse(result, operationId);
    }

    private async Task<T?> ReadRequestAsync<T>(CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(Request.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private IActionResult CastSalesMutationResponse(
        Result<CastSalesAdjustmentMutationResult> result,
        string operationId)
    {
        if (!result.Succeeded)
        {
            var status = result.FailureKind is ResultFailureKind.Conflict ? "conflict" : "unavailable";
            return StatusCode(
                status == "conflict" ? StatusCodes.Status409Conflict : StatusCodes.Status503ServiceUnavailable,
                new
                {
                    succeeded = false,
                    status,
                    operationId,
                    message = result.ErrorMessage ?? "キャスト売上額調整を保存できませんでした。"
                });
        }

        var output = result.Value;
        var payload = new
        {
            succeeded = output.Status == "confirmed",
            output.Status,
            output.OperationId,
            output.Message,
            output.BusinessDayId,
            output.BusinessDayRevision,
            output.SavedAdjustments,
            output.Detail,
            output.OverviewStatus,
            output.DashboardDelta,
            output.LatestOverview
        };
        return output.Status switch
        {
            "confirmed" => new JsonResult(payload),
            "conflict" => StatusCode(StatusCodes.Status409Conflict, payload),
            _ => BadRequest(payload)
        };
    }

    private static bool TryValidateCastSalesSaveRequest(
        CastSalesSaveRequest request,
        out string operationId,
        out string message)
    {
        operationId = NormalizeCastSalesOperationId(request.OperationId);
        if (operationId.Length == 0 ||
            request.ExpectedBusinessDayId <= 0 || request.ExpectedBusinessDayRevision < 0 ||
            request.SlipId <= 0 || request.ExpectedCheckoutId <= 0 ||
            request.ExpectedSlipVersion == default || request.ExpectedCheckoutVersion == default)
        {
            message = "営業日または会計情報が正しくありません。";
            return false;
        }

        if (!ValidCastSalesAdjustments(request.Adjustments))
        {
            message = "指名キャスト全員の売上額を0円以上の整数で入力してください。";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool TryValidateCastSalesConfirmRequest(
        CastSalesConfirmAllRequest request,
        out string operationId,
        out string message)
    {
        operationId = NormalizeCastSalesOperationId(request.OperationId);
        if (operationId.Length == 0 || request.ExpectedBusinessDayId <= 0 ||
            request.ExpectedBusinessDayRevision < 0 || request.Slips is not { Count: >= 1 and <= 100 } ||
            request.Slips.Select(slip => slip.SlipId).Distinct().Count() != request.Slips.Count)
        {
            message = "確認する伝票の内容が正しくありません。";
            return false;
        }

        if (request.Slips.Any(slip =>
                slip.SlipId <= 0 || slip.ExpectedCheckoutId <= 0 ||
                slip.ExpectedSlipVersion == default || slip.ExpectedCheckoutVersion == default ||
                !ValidCastSalesAdjustments(slip.Adjustments)))
        {
            message = "指名キャスト全員の売上額を0円以上の整数で入力してください。";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool ValidCastSalesAdjustments(IReadOnlyList<CastSalesAdjustmentRequest>? adjustments)
    {
        return adjustments is { Count: > 0 and <= 100 } &&
               adjustments.Select(value => value.SlipCastId).Distinct().Count() == adjustments.Count &&
               adjustments.All(value => value.SlipCastId > 0 &&
                                        value.SalesAmount is >= 0 and <= 999999999999m &&
                                        decimal.Truncate(value.SalesAmount) == value.SalesAmount);
    }

    private static string NormalizeCastSalesOperationId(string? value)
    {
        return Guid.TryParse(value, out var operationId) ? operationId.ToString("D") : string.Empty;
    }

    private static object CastSalesValidationPayload(string? operationId, string? message) => new
    {
        succeeded = false,
        status = "validation_error",
        operationId,
        message = string.IsNullOrWhiteSpace(message) ? "入力内容が正しくありません。" : message
    };

    public sealed record CastSalesAdjustmentRequest(long SlipCastId, decimal SalesAmount);

    public sealed record CastSalesSaveRequest(
        string OperationId,
        long ExpectedBusinessDayId,
        long ExpectedBusinessDayRevision,
        long SlipId,
        DateTimeOffset ExpectedSlipVersion,
        long ExpectedCheckoutId,
        DateTimeOffset ExpectedCheckoutVersion,
        string SourceAmountType,
        string SplitMode,
        IReadOnlyList<CastSalesAdjustmentRequest>? Adjustments);

    public sealed record CastSalesConfirmSlipRequest(
        long SlipId,
        DateTimeOffset ExpectedSlipVersion,
        long ExpectedCheckoutId,
        DateTimeOffset ExpectedCheckoutVersion,
        string SourceAmountType,
        string SplitMode,
        IReadOnlyList<CastSalesAdjustmentRequest>? Adjustments);

    public sealed record CastSalesConfirmAllRequest(
        string OperationId,
        long ExpectedBusinessDayId,
        long ExpectedBusinessDayRevision,
        IReadOnlyList<CastSalesConfirmSlipRequest>? Slips);
}
