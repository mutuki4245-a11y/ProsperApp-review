using System.Text.Json.Serialization;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseCheckoutRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IStoreClock storeClock) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), ICheckoutRepository
{
    public async Task<ConfirmCheckoutResult> ConfirmCheckoutAsync(long slipId, CheckoutInputModel input, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return ConfirmCheckoutResult.Failed("Supabase SecretKeyが未設定です。会計処理を実行できません。");
        }

        if (slipId <= 0 || input.ClosedAt is null)
        {
            return ConfirmCheckoutResult.Failed("会計に必要な入力が不足しています。");
        }

        var payments = input.Payments
            .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.MethodCode) && x.Amount > 0)
            .Select(x => new CheckoutPaymentPayload(x.MethodCode.Trim(), x.Amount))
            .ToArray();

        if (payments.Length == 0)
        {
            return ConfirmCheckoutResult.Failed("決済方法を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "confirm_store_checkout",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_closed_at = storeClock.ToStoreDateTimeOffset(input.ClosedAt.Value),
                p_payments = payments,
                p_received_amount = input.ReceivedAmount
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return ConfirmCheckoutResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var checkoutId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "checkout_id") : null;
        var changeAmount = result.Rows.Count > 0 ? ReadDecimal(result.Rows[0], "change_amount") ?? 0 : 0;
        return checkoutId is null
            ? ConfirmCheckoutResult.Failed("会計IDを取得できません。")
            : ConfirmCheckoutResult.Success(checkoutId.Value, changeAmount);
    }

    private sealed record CheckoutPaymentPayload(
        [property: JsonPropertyName("method_code")] string MethodCode,
        [property: JsonPropertyName("amount")] decimal Amount);

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "会計を確定できません。";
        }

        if (rawError.Contains("store_checkout_slip_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "会計できる伝票を確認してください。";
        }

        if (rawError.Contains("checkout_already_exists", StringComparison.OrdinalIgnoreCase))
        {
            return "この伝票はすでに会計済みです。";
        }

        if (rawError.Contains("invalid_closed_at", StringComparison.OrdinalIgnoreCase))
        {
            return "退店時刻は入店時刻以降で入力してください。";
        }

        if (rawError.Contains("invalid_checkout_total", StringComparison.OrdinalIgnoreCase))
        {
            return "決済金額の合計が会計額と一致しません。";
        }

        if (rawError.Contains("invalid_received_amount", StringComparison.OrdinalIgnoreCase))
        {
            return "受取額を確認してください。";
        }

        if (rawError.Contains("invalid_checkout_payment", StringComparison.OrdinalIgnoreCase))
        {
            return "決済方法と金額を確認してください。";
        }

        return $"会計を確定できません。{rawError}";
    }

}
