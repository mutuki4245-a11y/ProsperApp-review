using System.Text.Json;
using ProsperApp.Features.DailyReports;
using ProsperApp.Features.Shared;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class SupabaseDailyReportRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IDailyReportRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<DailyReportDocument>> GetAsync(long? businessDayId, CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.get_business_day_daily_report",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = NormalizeId(businessDayId)
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<DailyReportDocument>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "日報を取得できませんでした。");
        }

        if (result.Value.Count == 0 ||
            !result.Value[0].TryGetProperty("report", out var report) ||
            report.ValueKind != JsonValueKind.Object)
        {
            return Result<DailyReportDocument>.Failure(
                ResultFailureKind.InvalidResponse,
                "日報の応答形式が正しくありません。");
        }

        try
        {
            var document = report.Deserialize<DailyReportDocument>(JsonOptions);
            if (document is null ||
                document.BusinessDay.BusinessDayId <= 0 ||
                string.IsNullOrWhiteSpace(document.SchemaVersion))
            {
                return Result<DailyReportDocument>.Failure(
                    ResultFailureKind.InvalidResponse,
                    "日報の必須情報がありません。");
            }

            return Result<DailyReportDocument>.Success(document);
        }
        catch (JsonException ex)
        {
            return Result<DailyReportDocument>.Failure(
                ResultFailureKind.InvalidResponse,
                $"日報を読み取れませんでした。{ex.Message}");
        }
    }
}
