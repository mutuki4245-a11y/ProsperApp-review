using System.Text.Json;
using ProsperApp.Features.SalesHistory;
using ProsperApp.Features.Shared;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class SupabaseSalesHistoryRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), ISalesHistoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<SalesHistoryPage>> GetPageAsync(
        SalesHistoryQuery query,
        DateOnly currentBusinessDate,
        CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.get_sales_history_page",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_from_business_date = query.FromBusinessDate,
                p_to_business_date = query.ToBusinessDate,
                p_before_business_date = query.BeforeBusinessDate,
                p_before_business_day_id = NormalizeId(query.BeforeBusinessDayId),
                p_limit = query.PageSize + 1,
                p_current_business_date = currentBusinessDate
            },
            ct);
        if (!result.Succeeded)
        {
            return Failure<SalesHistoryPage>(result, "営業履歴を取得できませんでした。");
        }

        var parsed = result.Value.Select(ParseSummary).ToList();
        if (parsed.Any(x => x is null))
        {
            return Result<SalesHistoryPage>.Failure(
                ResultFailureKind.InvalidResponse,
                "営業履歴の応答形式が正しくありません。");
        }

        var items = parsed.Select(x => x!).Take(query.PageSize).ToList();
        var last = items.LastOrDefault();
        return Result<SalesHistoryPage>.Success(new SalesHistoryPage
        {
            Items = items,
            HasMore = parsed.Count > query.PageSize,
            NextBeforeBusinessDate = last?.BusinessDate,
            NextBeforeBusinessDayId = last?.BusinessDayId
        });
    }

    public async Task<Result<SalesHistoryCorrectionEditor>> GetCorrectionEditorAsync(
        long businessDayId,
        DateOnly currentBusinessDate,
        CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.get_sales_history_correction_editor",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_current_business_date = currentBusinessDate
            },
            ct);
        if (!result.Succeeded)
        {
            return Failure<SalesHistoryCorrectionEditor>(result, "補正画面を取得できませんでした。");
        }

        if (result.Value.Count != 1 ||
            !result.Value[0].TryGetProperty("editor", out var editorElement) ||
            editorElement.ValueKind != JsonValueKind.Object)
        {
            return Result<SalesHistoryCorrectionEditor>.Failure(
                ResultFailureKind.InvalidResponse,
                "補正画面の応答形式が正しくありません。");
        }

        try
        {
            var editor = editorElement.Deserialize<SalesHistoryCorrectionEditor>(JsonOptions);
            return editor is not null && editor.BusinessDayId > 0
                ? Result<SalesHistoryCorrectionEditor>.Success(editor)
                : Result<SalesHistoryCorrectionEditor>.Failure(
                    ResultFailureKind.InvalidResponse,
                    "補正画面の必須情報がありません。");
        }
        catch (JsonException ex)
        {
            return Result<SalesHistoryCorrectionEditor>.Failure(
                ResultFailureKind.InvalidResponse,
                $"補正画面を読み取れませんでした。{ex.Message}");
        }
    }

    public async Task<Result<SalesHistoryCorrectionResult>> SaveCorrectionAsync(
        SalesHistoryCorrectionMutation mutation,
        CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.save_sales_history_correction_v1",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = mutation.OperationId,
                p_business_day_id = mutation.BusinessDayId,
                p_expected_snapshot_version = mutation.ExpectedSnapshotVersion,
                p_current_business_date = mutation.CurrentBusinessDate,
                p_actor_email = mutation.ActorEmail,
                p_reason = mutation.Reason,
                p_correction_payload = new
                {
                    memo = mutation.Memo,
                    drink_delivery_amount = mutation.DrinkDeliveryAmount,
                    cast_entries = mutation.CastEntries.Select(x => new
                    {
                        cast_id = x.CastId,
                        is_selected = x.IsSelected,
                        clock_in_time = x.ClockInTime,
                        clock_out_time = x.ClockOutTime,
                        uses_send_service = x.UsesSendService
                    }).ToArray(),
                    staff_entries = mutation.StaffEntries.Select(x => new
                    {
                        staff_id = x.StaffId,
                        is_selected = x.IsSelected,
                        clock_in_time = x.ClockInTime,
                        clock_out_time = x.ClockOutTime,
                        uses_send_service = x.UsesSendService
                    }).ToArray(),
                    drink_back_adjustments = mutation.DrinkBackAdjustments.Select(x => new
                    {
                        cast_id = x.CastId,
                        is_active = x.IsActive,
                        adjustment_amount = x.AdjustmentAmount
                    }).ToArray(),
                    cast_sales_slips = mutation.CastSalesSlips.Select(slip => new
                    {
                        slip_id = slip.SlipId,
                        source_amount_type = slip.SourceAmountType,
                        split_mode = slip.SplitMode,
                        adjustments = slip.Adjustments.Select(x => new
                        {
                            slip_cast_id = x.SlipCastId,
                            sales_amount = x.SalesAmount
                        }).ToArray()
                    }).ToArray()
                }
            },
            ct);
        if (!result.Succeeded)
        {
            return Failure<SalesHistoryCorrectionResult>(result, "営業日の補正を保存できませんでした。");
        }

        if (result.Value.Count != 1)
        {
            return Result<SalesHistoryCorrectionResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "営業日補正の保存応答が正しくありません。");
        }

        var row = result.Value[0];
        var status = ReadString(row, "status") ?? string.Empty;
        var message = ReadString(row, "message");
        if (status is "validation_error" or "permission_denied" or "conflict")
        {
            var failureKind = status switch
            {
                "validation_error" => ResultFailureKind.InvalidInput,
                "permission_denied" => ResultFailureKind.PermissionDenied,
                _ => ResultFailureKind.Conflict
            };
            return Result<SalesHistoryCorrectionResult>.Failure(
                failureKind,
                message ?? "営業日の補正を保存できませんでした。");
        }

        if (status is not ("confirmed" or "unchanged") ||
            string.IsNullOrWhiteSpace(ReadString(row, "operation_id")))
        {
            return Result<SalesHistoryCorrectionResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "営業日補正の保存応答が正しくありません。");
        }

        return Result<SalesHistoryCorrectionResult>.Success(new SalesHistoryCorrectionResult
        {
            Status = status,
            OperationId = ReadString(row, "operation_id")!,
            Message = message,
            BusinessDayId = ReadLong(row, "business_day_id") ?? 0,
            SnapshotVersion = (int)(ReadLong(row, "snapshot_version") ?? 0),
            CorrectedAt = ReadDateTimeOffset(row, "corrected_at"),
            ChangedSections = ReadStringArray(row, "changed_sections")
        });
    }

    private static SalesHistorySummary? ParseSummary(JsonElement row)
    {
        var businessDayId = ReadLong(row, "business_day_id") ?? 0;
        var businessDate = ReadDateOnly(row, "business_date");
        var status = ReadString(row, "status");
        if (businessDayId <= 0 || businessDate is null || status is not ("open" or "closed"))
        {
            return null;
        }

        return new SalesHistorySummary
        {
            BusinessDayId = businessDayId,
            BusinessDate = businessDate.Value,
            Status = status,
            OpenedAt = ReadDateTimeOffset(row, "opened_at"),
            ClosedAt = ReadDateTimeOffset(row, "closed_at"),
            SalesAmount = ReadDecimal(row, "sales_amount") ?? 0,
            CustomerCount = (int)(ReadLong(row, "customer_count") ?? 0),
            SlipCount = (int)(ReadLong(row, "slip_count") ?? 0),
            Memo = ReadString(row, "memo"),
            LatestSnapshotVersion = ReadLong(row, "latest_snapshot_version") is { } version ? (int)version : null,
            ReportAvailable = ReadBool(row, "report_available") ?? false,
            IsCorrected = ReadBool(row, "is_corrected") ?? false,
            CanEdit = ReadBool(row, "can_edit") ?? false,
            EditReason = ReadString(row, "edit_reason")
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement row, string propertyName)
    {
        return row.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList()
            : [];
    }

    private static Result<T> Failure<T>(
        Result<IReadOnlyList<JsonElement>> source,
        string fallbackMessage)
    {
        var message = source.ErrorMessage ?? fallbackMessage;
        var kind = source.FailureKind ?? ResultFailureKind.Unavailable;
        if (message is "sales_history_business_day_not_found" or "business_day_snapshot_not_found")
        {
            kind = ResultFailureKind.NotFound;
        }
        else if (message is "business_day_operation_id_reused" or "business_day_revision_conflict")
        {
            kind = ResultFailureKind.Conflict;
        }
        else if (message is "invalid_sales_history_correction_input" or "invalid_sales_history_editor_input")
        {
            kind = ResultFailureKind.InvalidInput;
        }

        return Result<T>.Failure(kind, message);
    }
}
