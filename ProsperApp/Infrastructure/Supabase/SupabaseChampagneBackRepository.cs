using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class SupabaseChampagneBackRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IChampagneBackRepository
{
    public async Task<Result<ChampagneBackOverview>> GetOverviewAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<ChampagneBackOverview>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。シャンパンバックを取得できません。");
        }

        if (businessDayId <= 0)
        {
            return Result<ChampagneBackOverview>.Failure(
                ResultFailureKind.InvalidInput,
                "営業日情報が正しくありません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_business_day_champagne_back_overview",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<ChampagneBackOverview>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "シャンパンバックを取得できませんでした。");
        }

        if (result.Value.Count == 0 ||
            !TryReadJsonProperty(result.Value[0], "status", JsonValueKind.Object, out var statusElement) ||
            !TryReadJsonProperty(result.Value[0], "casts", JsonValueKind.Array, out var castsElement))
        {
            return Result<ChampagneBackOverview>.Failure(
                ResultFailureKind.InvalidResponse,
                "シャンパンバックの応答形式が正しくありません。");
        }

        return Result<ChampagneBackOverview>.Success(new ChampagneBackOverview
        {
            Status = new ChampagneBackStatus
            {
                RequiredCastCount = (int)(ReadLong(statusElement, "required_cast_count") ?? 0),
                CompletedCastCount = (int)(ReadLong(statusElement, "completed_cast_count") ?? 0),
                MissingCastCount = (int)(ReadLong(statusElement, "missing_cast_count") ?? 0),
                TotalBackAmount = ReadDecimal(statusElement, "total_back_amount") ?? 0
            },
            Casts = castsElement
                .EnumerateArray()
                .Select(row => new ChampagneBackCastRow
                {
                    CastId = ReadLong(row, "cast_id") ?? 0,
                    DisplayName = ReadString(row, "display_name") ?? string.Empty,
                    DepartmentName = ReadString(row, "department_name"),
                    BackAmount = ReadDecimal(row, "back_amount"),
                    IsSaved = ReadBool(row, "is_saved") ?? false
                })
                .Where(row => row.CastId > 0 && !string.IsNullOrWhiteSpace(row.DisplayName))
                .ToList()
        });
    }

    public async Task<ChampagneBackSaveResult> SaveAsync(ChampagneBackSaveInput input, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return ChampagneBackSaveResult.Failed("Supabase Edge Function設定が未設定です。シャンパンバックを保存できません。");
        }

        if (input.BusinessDayId is null or <= 0)
        {
            return ChampagneBackSaveResult.Failed("営業日情報が正しくありません。");
        }

        var backs = input.Casts
            .Where(cast => cast.CastId > 0)
            .GroupBy(cast => cast.CastId)
            .Select(group => group.Last())
            .Select(cast => new ChampagneBackPayload(cast.CastId, cast.BackAmount))
            .ToArray();
        if (backs.Length == 0)
        {
            return ChampagneBackSaveResult.Failed("出勤キャスト全員のシャンパンバックを入力してください。");
        }

        if (backs.Any(back => back.BackAmount < 0 || decimal.Truncate(back.BackAmount) != back.BackAmount))
        {
            return ChampagneBackSaveResult.Failed("シャンパン追加バックは0円以上の整数で入力してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.save_business_day_champagne_backs",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = input.BusinessDayId.Value,
                p_backs = backs
            },
            ct);
        if (!result.Succeeded || result.Rows.Count == 0)
        {
            return ChampagneBackSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return ChampagneBackSaveResult.Success(
            (int)(ReadLong(result.Rows[0], "saved_cast_count") ?? 0),
            ReadDecimal(result.Rows[0], "total_back_amount") ?? 0);
    }

    private sealed record ChampagneBackPayload(
        [property: JsonPropertyName("cast_id")] long CastId,
        [property: JsonPropertyName("back_amount")] decimal BackAmount);

    private static bool TryReadJsonProperty(
        JsonElement row,
        string propertyName,
        JsonValueKind expectedKind,
        out JsonElement value)
    {
        if (row.TryGetProperty(propertyName, out value) && value.ValueKind == expectedKind)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "シャンパンバックを保存できませんでした。";
        }

        if (rawError.Contains("champagne_back_attendance_changed", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤キャストが更新されています。画面を再読み込みしてから保存してください。";
        }

        if (rawError.Contains("champagne_back_attendance_required", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤キャストを登録してからシャンパンバックを入力してください。";
        }

        if (rawError.Contains("invalid_champagne_back_payload", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("duplicate_champagne_back_cast", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤キャスト全員のシャンパン追加バックを0円以上の整数で入力してください。";
        }

        if (rawError.Contains("business_day_not_open", StringComparison.OrdinalIgnoreCase))
        {
            return "営業日情報が更新されています。画面を再読み込みしてください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"シャンパンバックを保存できませんでした。{rawError}";
    }
}
