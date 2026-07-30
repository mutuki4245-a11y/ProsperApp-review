using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseCheckoutRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), ICheckoutRepository
{
    public async Task<Result<IReadOnlyList<CheckoutPaymentMethod>>> GetPaymentMethodsAsync(CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.get_payment_methods",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);
        if (!result.Succeeded)
        {
            return Result<IReadOnlyList<CheckoutPaymentMethod>>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "決済方法を取得できませんでした。");
        }

        var methods = result.Value
            .Select(row => new CheckoutPaymentMethod
            {
                MethodCode = ReadString(row, "method_code") ?? string.Empty,
                MethodName = ReadString(row, "method_name") ?? string.Empty,
                RequiresReceivedAmount = ReadBool(row, "requires_received_amount") ?? false,
                SortOrder = (int)(ReadLong(row, "sort_order") ?? 0)
            })
            .Where(method =>
                !string.IsNullOrWhiteSpace(method.MethodCode) &&
                !string.IsNullOrWhiteSpace(method.MethodName))
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.MethodCode, StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<CheckoutPaymentMethod>>.Success(methods);
    }

    public async Task<CheckoutStatementResult> IssueCheckoutStatementAsync(long slipId, DateTimeOffset closedAt, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return CheckoutStatementResult.Failed("Supabase Edge Function設定が未設定です。会計伝票を出力できません。");
        }

        if (slipId <= 0)
        {
            return CheckoutStatementResult.Failed("会計対象の伝票を確認してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.issue_checkout_statement",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_closed_at = closedAt
            },
            ct);

        return ReadStatementResult(result, "会計伝票を出力できませんでした。");
    }

    public async Task<CheckoutStatementResult> GetCheckoutStatementPrintDataAsync(long slipId, CancellationToken ct)
    {
        if (!HasRpcAccess() || slipId <= 0)
        {
            return CheckoutStatementResult.Failed("会計伝票を復旧できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_checkout_statement_print_data",
            new { p_department_id = CurrentStoreDepartmentId, p_slip_id = slipId },
            ct);
        return ReadStatementResult(result, "会計伝票を復旧できませんでした。");
    }

    public async Task<ReleaseCheckoutReadyResult> ReleaseCheckoutReadyAsync(long slipId, CancellationToken ct)
    {
        if (!HasRpcAccess() || slipId <= 0)
        {
            return ReleaseCheckoutReadyResult.Failed("会計準備を解除できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.release_checkout_ready",
            new { p_department_id = CurrentStoreDepartmentId, p_slip_id = slipId },
            ct);
        return result.Succeeded && result.Rows.Count > 0
            ? ReleaseCheckoutReadyResult.Success()
            : ReleaseCheckoutReadyResult.Failed(ToFriendlyError(result.ErrorMessage));
    }

    public async Task<ConfirmCheckoutResult> ConfirmCheckoutAsync(
        long slipId,
        IReadOnlyList<CheckoutPaymentInputModel> payments,
        decimal? receivedAmount,
        CancellationToken ct)
    {
        if (!HasRpcAccess() || slipId <= 0)
        {
            return ConfirmCheckoutResult.Failed("会計対象の伝票を確認してください。");
        }

        var payload = (payments ?? [])
            .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.MethodCode))
            .Select(x => new CheckoutPaymentPayload(x.MethodCode.Trim(), x.Amount))
            .ToArray();
        var result = await RpcClient.PostArrayAsync(
            "store.confirm_checkout",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_payments = payload,
                p_received_amount = receivedAmount
            },
            ct);

        if (!result.Succeeded || result.Rows.Count == 0)
        {
            return ConfirmCheckoutResult.Failed(ToFriendlyError(result.ErrorMessage), RequiresCheckoutReload(result.ErrorMessage));
        }

        var row = result.Rows[0];
        var checkoutId = ReadLong(row, "checkout_id");
        if (checkoutId is null || !TryReadJson(row, "print_data", out var printData))
        {
            return ConfirmCheckoutResult.Failed("会計確定結果を取得できません。");
        }

        return ConfirmCheckoutResult.Success(checkoutId.Value, ReadDecimal(row, "change_amount") ?? 0, printData);
    }

    public async Task<ReceiptPrintDataResult> GetCheckoutReceiptPrintDataAsync(long slipId, CancellationToken ct)
    {
        if (!HasRpcAccess() || slipId <= 0)
        {
            return ReceiptPrintDataResult.Failed("領収書を取得できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_checkout_receipt_print_data",
            new { p_department_id = CurrentStoreDepartmentId, p_slip_id = slipId },
            ct);
        if (!result.Succeeded || result.Rows.Count == 0)
        {
            return ReceiptPrintDataResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var row = result.Rows[0];
        var checkoutId = ReadLong(row, "checkout_id");
        return checkoutId is null || !TryReadJson(row, "print_data", out var printData)
            ? ReceiptPrintDataResult.Failed("領収書データを取得できません。")
            : ReceiptPrintDataResult.Success(checkoutId.Value, printData);
    }

    public async Task<CancelCheckoutResult> CancelCheckoutAsync(long slipId, CancellationToken ct)
    {
        if (!HasRpcAccess() || slipId <= 0)
        {
            return CancelCheckoutResult.Failed("会計取消する伝票を確認してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.cancel_checkout",
            new { p_department_id = CurrentStoreDepartmentId, p_slip_id = slipId },
            ct);
        var checkoutId = result.Succeeded && result.Rows.Count > 0 ? ReadLong(result.Rows[0], "checkout_id") : null;
        return checkoutId is null
            ? CancelCheckoutResult.Failed(ToFriendlyError(result.ErrorMessage))
            : CancelCheckoutResult.Success(checkoutId.Value);
    }

    private static CheckoutStatementResult ReadStatementResult(SupabaseRpcResult result, string fallback)
    {
        if (!result.Succeeded || result.Rows.Count == 0 ||
            !TryReadJson(result.Rows[0], "print_data", out var printData) ||
            !TryReadJson(result.Rows[0], "review_data", out var reviewData))
        {
            return CheckoutStatementResult.Failed(string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? fallback
                : ToFriendlyError(result.ErrorMessage));
        }

        return CheckoutStatementResult.Success(printData, reviewData);
    }

    private static bool TryReadJson(JsonElement row, string propertyName, out JsonElement value)
    {
        value = default;
        if (!row.TryGetProperty(propertyName, out var raw) ||
            raw.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return false;
        }

        value = raw.Clone();
        return true;
    }

    private sealed record CheckoutPaymentPayload(
        [property: JsonPropertyName("method_code")] string MethodCode,
        [property: JsonPropertyName("amount")] decimal Amount);

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError)) return "会計処理を実行できません。";
        if (rawError.Contains("checkout_ready_not_found", StringComparison.OrdinalIgnoreCase)) return "会計準備中の伝票を確認してください。";
        if (rawError.Contains("checkout_statement_slip_not_found", StringComparison.OrdinalIgnoreCase)) return "会計伝票を出力できる伝票を確認してください。";
        if (rawError.Contains("checkout_already_exists", StringComparison.OrdinalIgnoreCase)) return "この伝票はすでに会計済みです。";
        if (rawError.Contains("invalid_closed_at", StringComparison.OrdinalIgnoreCase)) return "退店時刻は伝票とすべてのお客様の入店時刻より後にしてください。";
        if (rawError.Contains("invalid_checkout_total", StringComparison.OrdinalIgnoreCase)) return "決済金額の合計が会計額と一致しません。";
        if (rawError.Contains("invalid_received_amount", StringComparison.OrdinalIgnoreCase)) return "受取額を確認してください。";
        if (rawError.Contains("multiple_received_amount_payment_methods", StringComparison.OrdinalIgnoreCase)) return "受取額を入力する決済方法は1つだけ選択してください。";
        if (rawError.Contains("duplicate_checkout_payment_method", StringComparison.OrdinalIgnoreCase)) return "同じ決済方法を重複して入力できません。";
        if (rawError.Contains("invalid_checkout_payment", StringComparison.OrdinalIgnoreCase)) return "決済方法と金額を確認してください。";
        return $"会計処理を実行できません。{rawError}";
    }

    private static bool RequiresCheckoutReload(string? rawError) =>
        !string.IsNullOrWhiteSpace(rawError) &&
        (rawError.Contains("checkout_ready_not_found", StringComparison.OrdinalIgnoreCase) ||
         rawError.Contains("checkout_already_exists", StringComparison.OrdinalIgnoreCase));
}
