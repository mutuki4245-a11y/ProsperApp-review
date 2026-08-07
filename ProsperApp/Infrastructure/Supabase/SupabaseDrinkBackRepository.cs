using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Closing;
using ProsperApp.Features.Shared;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class SupabaseDrinkBackRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IDrinkBackRepository
{
    public async Task<Result<CurrentDrinkBackEditorState>> GetCurrentEditorAsync(CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.get_current_drink_back_editor",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);
        if (!result.Succeeded)
        {
            return Result<CurrentDrinkBackEditorState>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "ドリンクバック調整を取得できませんでした。");
        }

        if (result.Value.Count != 1 ||
            !TryReadObject(result.Value[0], "editor", out var editorElement) ||
            !TryParseEditor(editorElement, out var editor))
        {
            return Result<CurrentDrinkBackEditorState>.Failure(
                ResultFailureKind.InvalidResponse,
                "ドリンクバック調整の応答形式が正しくありません。");
        }

        var row = result.Value[0];
        var activeCasts = ReadArray(row, "active_casts")
            .Select(ParseCastOption)
            .Where(cast => cast.CastId > 0 && !string.IsNullOrWhiteSpace(cast.DisplayName))
            .ToList();
        return Result<CurrentDrinkBackEditorState>.Success(new CurrentDrinkBackEditorState(
            editor,
            ReadString(row, "cast_master_revision") ?? string.Empty,
            activeCasts));
    }

    public async Task<Result<DrinkBackSaveOutput>> SaveAsync(
        DrinkBackSaveMutation input,
        CancellationToken ct)
    {
        if (!Guid.TryParse(input.OperationId, out var operationId))
        {
            return Result<DrinkBackSaveOutput>.Failure(
                ResultFailureKind.InvalidInput,
                "保存操作IDが正しくありません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.save_drink_back_adjustments_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = operationId.ToString("D"),
                p_expected_business_day_id = input.ExpectedBusinessDayId,
                p_expected_business_day_revision = input.ExpectedBusinessDayRevision,
                p_required_adjustments = input.RequiredAdjustments.Select(ToPayload).ToArray(),
                p_optional_adjustments = input.OptionalAdjustments.Select(ToPayload).ToArray(),
                p_remove_cast_ids = input.RemoveCastIds.Distinct().ToArray()
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<DrinkBackSaveOutput>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                ToFriendlyError(result.ErrorMessage));
        }

        if (result.Value.Count != 1)
        {
            return Result<DrinkBackSaveOutput>.Failure(
                ResultFailureKind.InvalidResponse,
                "ドリンクバック調整の保存応答がありません。");
        }

        var row = result.Value[0];
        if (!TryReadObject(row, "editor", out var editorElement) ||
            !TryParseEditor(editorElement, out var editor) ||
            !TryReadObject(row, "drink_back_status", out var statusElement))
        {
            return Result<DrinkBackSaveOutput>.Failure(
                ResultFailureKind.InvalidResponse,
                "ドリンクバック調整の保存応答形式が正しくありません。");
        }

        ClosingDashboardDrinkBackDelta? dashboardDelta = null;
        if (TryReadObject(row, "dashboard_delta", out var deltaElement) &&
            TryParseDashboardDelta(deltaElement, out var parsedDelta))
        {
            dashboardDelta = parsedDelta;
        }

        return Result<DrinkBackSaveOutput>.Success(new DrinkBackSaveOutput(
            ReadString(row, "status") ?? "validation_error",
            ReadString(row, "operation_id") ?? operationId.ToString("D"),
            ReadString(row, "message"),
            editor,
            ParseStatus(statusElement),
            dashboardDelta,
            ReadLong(row, "business_day_revision") ?? editor.BusinessDayRevision));
    }

    public static bool TryParseEditor(JsonElement element, out DrinkBackEditorFragment editor)
    {
        editor = default!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var requiredRows = ReadArray(element, "required_rows").Select(ParseAdjustmentRow).ToList();
        var optionalRows = ReadArray(element, "optional_rows").Select(ParseAdjustmentRow).ToList();
        var status = TryReadObject(element, "status", out var statusElement)
            ? ParseStatus(statusElement)
            : new DrinkBackStatus(0, 0, 0, 0);
        editor = new DrinkBackEditorFragment(
            ReadLong(element, "department_id") ?? 0,
            ReadBool(element, "has_business_day") ?? false,
            ReadLong(element, "business_day_id"),
            ReadLong(element, "business_day_revision") ?? 0,
            ReadDateOnly(element, "business_date"),
            requiredRows,
            optionalRows,
            status,
            (int)(ReadLong(element, "optional_adjustment_count") ?? optionalRows.Count),
            ReadDateTimeOffset(element, "checked_at") ?? DateTimeOffset.UtcNow);
        return editor.DepartmentId > 0;
    }

    public static DrinkBackCastOption ParseCastOption(JsonElement element) => new(
        ReadLong(element, "cast_id") ?? 0,
        ReadString(element, "display_name") ?? string.Empty,
        ReadString(element, "department_name"));

    private static DrinkBackAdjustmentRow ParseAdjustmentRow(JsonElement element) => new(
        ReadLong(element, "cast_id") ?? 0,
        ReadString(element, "display_name") ?? string.Empty,
        ReadString(element, "department_name"),
        ReadLong(element, "adjustment_amount"),
        ReadBool(element, "is_saved") ?? false,
        ReadBool(element, "is_required") ?? false);

    private static DrinkBackStatus ParseStatus(JsonElement element) => new(
        (int)(ReadLong(element, "required_cast_count") ?? 0),
        (int)(ReadLong(element, "completed_cast_count") ?? 0),
        (int)(ReadLong(element, "missing_cast_count") ?? 0),
        ReadLong(element, "total_amount") ?? 0);

    private static bool TryParseDashboardDelta(
        JsonElement element,
        out ClosingDashboardDrinkBackDelta delta)
    {
        delta = default!;
        if (!TryReadObject(element, "drink_back_editor", out var editorElement) ||
            !TryParseEditor(editorElement, out var editor))
        {
            return false;
        }

        delta = new ClosingDashboardDrinkBackDelta(
            ReadLong(element, "department_id") ?? 0,
            ReadLong(element, "business_day_id") ?? 0,
            ReadLong(element, "business_day_revision") ?? 0,
            (int)(ReadLong(element, "drink_back_required_cast_count") ?? 0),
            (int)(ReadLong(element, "drink_back_completed_cast_count") ?? 0),
            (int)(ReadLong(element, "drink_back_missing_cast_count") ?? 0),
            ReadLong(element, "drink_back_total_amount") ?? 0,
            ReadBool(element, "can_close") ?? false,
            ReadStrings(element, "block_reasons"),
            editor,
            ReadDateTimeOffset(element, "checked_at") ?? DateTimeOffset.UtcNow);
        return delta.DepartmentId > 0 && delta.BusinessDayId > 0;
    }

    private static DrinkBackAdjustmentPayload ToPayload(DrinkBackAdjustmentInput input) =>
        new(input.CastId, input.AdjustmentAmount);

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : [];

    private static bool TryReadObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "ドリンクバック調整を保存できませんでした。";
        }

        if (rawError.Contains("business_day_revision_conflict", StringComparison.OrdinalIgnoreCase))
        {
            return "営業日または出勤キャストが更新されています。最新状態を確認してください。";
        }

        if (rawError.Contains("business_day_operation_id_reused", StringComparison.OrdinalIgnoreCase))
        {
            return "同じ保存操作IDに異なる内容が指定されました。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"ドリンクバック調整を保存できませんでした。{rawError}";
    }

    private sealed record DrinkBackAdjustmentPayload(
        [property: JsonPropertyName("cast_id")] long CastId,
        [property: JsonPropertyName("adjustment_amount")] long AdjustmentAmount);
}
