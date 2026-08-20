using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseCheckoutRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache,
    IStoreMasterBootstrapper masterBootstrapper) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), ICheckoutRepository
{
    private readonly IApplicationCache _cache = cache;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<Result<IReadOnlyList<CheckoutPaymentMethod>>> GetPaymentMethodsAsync(CancellationToken ct)
    {
        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.PaymentMethods(departmentId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<CheckoutPaymentMethod>? cachedMethods))
        {
            return Result<IReadOnlyList<CheckoutPaymentMethod>>.Success(cachedMethods ?? []);
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return Result<IReadOnlyList<CheckoutPaymentMethod>>.Failure(
                bootstrap.FailureKind ?? ResultFailureKind.Unavailable,
                bootstrap.ErrorMessage ?? "決済方法を取得できませんでした。");
        }

        var methods = StoreBootstrapJson.ReadArray(bootstrap.Value.Row, "payment_methods")
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

        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, methods, "決済方法");
        return Result<IReadOnlyList<CheckoutPaymentMethod>>.Success(methods);
    }

    public Task<Result<CheckoutMutationResult>> IssueCheckoutStatementV2Async(
        IssueCheckoutStatementV2Mutation mutation,
        CancellationToken ct)
    {
        if (!IsValidMutationIdentity(
                mutation.OperationId,
                mutation.ExpectedBusinessDayId,
                mutation.ExpectedBusinessDayRevision,
                mutation.SlipId))
        {
            return Task.FromResult(InvalidMutation("会計伝票の発行条件を確認してください。"));
        }

        return ExecuteMutationV2Async(
            "store.issue_checkout_statement_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = mutation.OperationId,
                p_expected_business_day_id = mutation.ExpectedBusinessDayId,
                p_expected_business_day_revision = mutation.ExpectedBusinessDayRevision,
                p_slip_id = mutation.SlipId,
                p_closed_at = mutation.ClosedAt
            },
            CheckoutMutationKind.Issue,
            ct);
    }

    public Task<Result<CheckoutMutationResult>> ReleaseCheckoutReadyV2Async(
        ReleaseCheckoutReadyV2Mutation mutation,
        CancellationToken ct)
    {
        if (!IsValidMutationIdentity(
                mutation.OperationId,
                mutation.ExpectedBusinessDayId,
                mutation.ExpectedBusinessDayRevision,
                mutation.SlipId))
        {
            return Task.FromResult(InvalidMutation("会計準備の解除条件を確認してください。"));
        }

        return ExecuteMutationV2Async(
            "store.release_checkout_ready_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = mutation.OperationId,
                p_expected_business_day_id = mutation.ExpectedBusinessDayId,
                p_expected_business_day_revision = mutation.ExpectedBusinessDayRevision,
                p_slip_id = mutation.SlipId
            },
            CheckoutMutationKind.Release,
            ct);
    }

    public Task<Result<CheckoutMutationResult>> ConfirmCheckoutV2Async(
        ConfirmCheckoutV2Mutation mutation,
        CancellationToken ct)
    {
        if (!IsValidMutationIdentity(
                mutation.OperationId,
                mutation.ExpectedBusinessDayId,
                mutation.ExpectedBusinessDayRevision,
                mutation.SlipId))
        {
            return Task.FromResult(InvalidMutation("会計確定条件を確認してください。"));
        }

        var payments = (mutation.Payments ?? [])
            .Where(payment => payment.IsSelected && !string.IsNullOrWhiteSpace(payment.MethodCode))
            .Select(payment => new CheckoutPaymentPayload(payment.MethodCode.Trim(), payment.Amount))
            .ToArray();

        return ExecuteMutationV2Async(
            "store.confirm_checkout_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = mutation.OperationId,
                p_expected_business_day_id = mutation.ExpectedBusinessDayId,
                p_expected_business_day_revision = mutation.ExpectedBusinessDayRevision,
                p_slip_id = mutation.SlipId,
                p_payments = payments,
                p_received_amount = mutation.ReceivedAmount
            },
            CheckoutMutationKind.Confirm,
            ct);
    }

    public Task<Result<CheckoutMutationResult>> CancelCheckoutV2Async(
        CancelCheckoutV2Mutation mutation,
        CancellationToken ct)
    {
        if (!IsValidMutationIdentity(
                mutation.OperationId,
                mutation.ExpectedBusinessDayId,
                mutation.ExpectedBusinessDayRevision,
                mutation.SlipId))
        {
            return Task.FromResult(InvalidMutation("会計取消条件を確認してください。"));
        }

        return ExecuteMutationV2Async(
            "store.cancel_checkout_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = mutation.OperationId,
                p_expected_business_day_id = mutation.ExpectedBusinessDayId,
                p_expected_business_day_revision = mutation.ExpectedBusinessDayRevision,
                p_slip_id = mutation.SlipId
            },
            CheckoutMutationKind.Cancel,
            ct);
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

    private async Task<Result<CheckoutMutationResult>> ExecuteMutationV2Async<TPayload>(
        string functionName,
        TPayload payload,
        CheckoutMutationKind kind,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<CheckoutMutationResult>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。会計処理を実行できません。");
        }

        var result = await RpcClient.PostArrayAsync(functionName, payload, ct);
        if (!result.Succeeded)
        {
            return RpcFailure<CheckoutMutationResult>(
                result.ErrorMessage,
                "会計処理を実行できませんでした。");
        }

        if (result.Rows.Count == 0 || !TryReadMutationResult(result.Rows[0], out var mutationResult))
        {
            return Result<CheckoutMutationResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "会計処理の結果を確認できませんでした。");
        }

        if (mutationResult.Confirmed && !HasRequiredConfirmedPayload(mutationResult, kind))
        {
            return Result<CheckoutMutationResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "会計処理の確定結果が不足しています。");
        }

        return Result<CheckoutMutationResult>.Success(mutationResult);
    }

    private static bool TryReadMutationResult(JsonElement row, out CheckoutMutationResult result)
    {
        result = default!;
        var operationId = ReadString(row, "operation_id");
        var status = ReadString(row, "status");
        if (string.IsNullOrWhiteSpace(operationId) ||
            status is not ("confirmed" or "conflict" or "validation_error"))
        {
            return false;
        }

        var errorCode = ReadString(row, "error_code");
        result = new CheckoutMutationResult(
            operationId,
            status,
            errorCode,
            string.IsNullOrWhiteSpace(errorCode) ? null : ToFriendlyError(errorCode),
            ReadLong(row, "slip_id"),
            ReadLong(row, "checkout_id"),
            ReadLong(row, "business_day_id"),
            ReadLong(row, "business_day_revision") ?? 0,
            ReadString(row, "current_slip_status"),
            ReadDecimal(row, "change_amount") ?? 0,
            ReadOptionalJson(row, "statement_print_data"),
            ReadOptionalJson(row, "statement_review_data"),
            ReadOptionalJson(row, "receipt_print_data"),
            ReadOptionalJson(row, "business_snapshot"));
        return true;
    }

    private static JsonElement? ReadOptionalJson(JsonElement row, string propertyName)
    {
        return TryReadJson(row, propertyName, out var value) ? value : null;
    }

    private static bool HasRequiredConfirmedPayload(
        CheckoutMutationResult result,
        CheckoutMutationKind kind)
    {
        if (result.SlipId is not > 0 ||
            result.BusinessDayId is not > 0 ||
            result.BusinessSnapshot is null)
        {
            return false;
        }

        return kind switch
        {
            CheckoutMutationKind.Issue =>
                result.CheckoutId is > 0 &&
                result.StatementPrintData is not null &&
                result.StatementReviewData is not null,
            CheckoutMutationKind.Release => true,
            CheckoutMutationKind.Confirm =>
                result.CheckoutId is > 0 &&
                result.ReceiptPrintData is not null,
            CheckoutMutationKind.Cancel => result.CheckoutId is > 0,
            _ => false
        };
    }

    private static bool IsValidMutationIdentity(
        string? operationId,
        long expectedBusinessDayId,
        long expectedBusinessDayRevision,
        long slipId)
    {
        return Guid.TryParseExact(operationId, "D", out _) &&
               expectedBusinessDayId > 0 &&
               expectedBusinessDayRevision >= 0 &&
               slipId > 0;
    }

    private static Result<CheckoutMutationResult> InvalidMutation(string message)
    {
        return Result<CheckoutMutationResult>.Failure(ResultFailureKind.InvalidInput, message);
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

    private enum CheckoutMutationKind
    {
        Issue,
        Release,
        Confirm,
        Cancel
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError)) return "会計処理を実行できません。";
        if (string.Equals(rawError, "checkout_ready_not_found", StringComparison.Ordinal)) return "会計準備中の伝票を確認してください。";
        if (string.Equals(rawError, "checkout_statement_slip_not_found", StringComparison.Ordinal)) return "会計伝票を出力できる伝票を確認してください。";
        if (string.Equals(rawError, "checkout_already_exists", StringComparison.Ordinal)) return "この伝票はすでに会計済みです。";
        if (string.Equals(rawError, "invalid_closed_at", StringComparison.Ordinal)) return "退店時刻は伝票とすべてのお客様の入店時刻より後にしてください。";
        if (string.Equals(rawError, "invalid_checkout_total", StringComparison.Ordinal)) return "決済金額の合計が会計額と一致しません。";
        if (string.Equals(rawError, "invalid_received_amount", StringComparison.Ordinal)) return "受取額を確認してください。";
        if (string.Equals(rawError, "multiple_received_amount_payment_methods", StringComparison.Ordinal)) return "受取額を入力する決済方法は1つだけ選択してください。";
        if (string.Equals(rawError, "duplicate_checkout_payment_method", StringComparison.Ordinal)) return "同じ決済方法を重複して入力できません。";
        if (string.Equals(rawError, "invalid_checkout_payment", StringComparison.Ordinal)) return "決済方法と金額を確認してください。";
        if (string.Equals(rawError, "checkout_unflushed_changes", StringComparison.Ordinal)) return "未保存の変更または別端末の更新があります。最新状態を確認してください。";
        if (string.Equals(rawError, "business_day_revision_conflict", StringComparison.Ordinal)) return "営業中データが更新されています。最新状態を確認してください。";
        if (string.Equals(rawError, "business_day_closed", StringComparison.Ordinal)) return "営業日はすでに締められています。";
        if (string.Equals(rawError, "checkout_statement_already_issued", StringComparison.Ordinal)) return "会計伝票はすでに発行されています。";
        if (string.Equals(rawError, "checkout_ready_already_released", StringComparison.Ordinal)) return "会計準備はすでに解除されています。";
        if (string.Equals(rawError, "checkout_already_confirmed", StringComparison.Ordinal)) return "この伝票はすでに会計確定済みです。";
        if (string.Equals(rawError, "checkout_already_cancelled", StringComparison.Ordinal)) return "この会計はすでに取り消されています。";
        if (string.Equals(rawError, "checkout_not_ready", StringComparison.Ordinal)) return "会計準備中の伝票ではありません。";
        if (string.Equals(rawError, "checkout_not_confirmed", StringComparison.Ordinal)) return "会計確定済みの伝票ではありません。";
        if (string.Equals(rawError, "checkout_target_not_found", StringComparison.Ordinal)) return "会計対象の伝票が見つかりません。";
        if (string.Equals(rawError, "checkout_business_day_conflict", StringComparison.Ordinal)) return "対象伝票の営業日が現在の営業日と一致しません。";
        if (string.Equals(rawError, "business_day_operation_id_reused", StringComparison.Ordinal)) return "同じ操作IDが別の会計操作に使われています。";
        if (string.Equals(rawError, "checkout_state_conflict", StringComparison.Ordinal)) return "伝票の会計状態が別端末で更新されています。";
        if (string.Equals(rawError, "invalid_checkout_mutation_input", StringComparison.Ordinal)) return "会計操作の入力を確認してください。";
        return "会計処理を実行できません。";
    }

}
