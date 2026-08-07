using System.Text.Json;
using ProsperApp.Features.Shared;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseReceiptRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IReceiptRepository
{
    public async Task<Result<ReceiptWorkQueue>> GetCurrentWorkQueueAsync(string? resumeCursor, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<ReceiptWorkQueue>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。領収書作業キューを取得できません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_current_receipt_work_queue",
            new { p_department_id = CurrentStoreDepartmentId, p_resume_cursor = resumeCursor },
            ct);
        if (!result.Succeeded || result.Value.Count == 0)
        {
            return Result<ReceiptWorkQueue>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                ToFriendlyError(result.ErrorMessage));
        }

        var row = result.Value[0];
        var businessDay = row.TryGetProperty("business_day", out var businessDayJson) &&
                          businessDayJson.ValueKind == JsonValueKind.Object
            ? ParseBusinessDay(businessDayJson)
            : null;
        var workItem = row.TryGetProperty("work_item", out var workItemJson) &&
                       workItemJson.ValueKind == JsonValueKind.Object
            ? ParsePendingItems([workItemJson]).FirstOrDefault()
            : null;
        var buffer = row.TryGetProperty("buffer", out var bufferJson) &&
                     bufferJson.ValueKind == JsonValueKind.Array
            ? ParsePendingItems(bufferJson.EnumerateArray().ToArray())
            : [];
        var advanceCastOptions = row.TryGetProperty("advance_casts", out var castsJson) &&
                                 castsJson.ValueKind == JsonValueKind.Array
            ? castsJson.EnumerateArray()
                .Select(ParseClosingAttendanceItem)
                .Where(item => item.IsCast && item.AttendanceStatus is "scheduled" or "checked_in" or "checked_out")
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture)
                .ToList()
            : [];

        return Result<ReceiptWorkQueue>.Success(new ReceiptWorkQueue(
            ReadString(row, "queue_revision") ?? string.Empty,
            (int)(ReadLong(row, "pending_count") ?? 0),
            businessDay,
            workItem,
            buffer,
            ReadString(row, "resume_cursor"),
            advanceCastOptions));
    }

    public async Task<Result<ReceiptWorkQueueAdvanceResult>> AdvanceWorkQueueAsync(
        ReceiptWorkQueueAdvanceInput input,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<ReceiptWorkQueueAdvanceResult>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。領収書を更新できません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.advance_receipt_work_queue_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = input.OperationId,
                p_action = input.Action,
                p_work_item_token = input.WorkItemToken,
                p_document_id = input.DocumentId,
                p_payment_date = input.PaymentDate,
                p_amount = input.Amount,
                p_account_subject = input.AccountSubject?.Trim(),
                p_description = input.Description?.Trim(),
                p_group_code = input.GroupCode?.Trim(),
                p_advance_cast_id = input.AdvanceCastId
            },
            ct);
        if (!result.Succeeded || result.Value.Count == 0 ||
            !result.Value[0].TryGetProperty("response", out var response) ||
            response.ValueKind != JsonValueKind.Object)
        {
            return Result<ReceiptWorkQueueAdvanceResult>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                ToFriendlyError(result.ErrorMessage));
        }

        var queue = ParseWorkQueue(response);
        var output = new ReceiptWorkQueueAdvanceResult(
            ReadString(response, "status") ?? "unavailable",
            ReadString(response, "document_id"),
            ReadString(response, "message"),
            queue,
            (int)(ReadLong(response, "pending_receipt_count") ?? queue.PendingCount));
        return Result<ReceiptWorkQueueAdvanceResult>.Success(output);
    }

    public async Task<Result<bool>> IsPendingDriveFileAllowedAsync(string driveFileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(driveFileId))
        {
            return Result<bool>.Failure(ResultFailureKind.InvalidInput, "DriveファイルIDがありません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.is_pending_receipt_drive_file_allowed",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_drive_file_id = driveFileId.Trim()
            },
            ct);
        if (!result.Succeeded || result.Value.Count == 0)
        {
            return Result<bool>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "未処理領収書を確認できませんでした。");
        }

        return Result<bool>.Success(ReadBool(result.Value[0], "is_allowed") ?? false);
    }

    private static ReceiptWorkQueue ParseWorkQueue(JsonElement row)
    {
        var businessDay = row.TryGetProperty("business_day", out var businessDayJson) &&
                          businessDayJson.ValueKind == JsonValueKind.Object
            ? ParseBusinessDay(businessDayJson)
            : null;
        var workItem = row.TryGetProperty("work_item", out var workItemJson) &&
                       workItemJson.ValueKind == JsonValueKind.Object
            ? ParsePendingItems([workItemJson]).FirstOrDefault()
            : null;
        var buffer = row.TryGetProperty("buffer", out var bufferJson) &&
                     bufferJson.ValueKind == JsonValueKind.Array
            ? ParsePendingItems(bufferJson.EnumerateArray().ToArray())
            : [];
        var advanceCastOptions = row.TryGetProperty("advance_casts", out var castsJson) &&
                                 castsJson.ValueKind == JsonValueKind.Array
            ? castsJson.EnumerateArray()
                .Select(ParseClosingAttendanceItem)
                .Where(item => item.IsCast && item.AttendanceStatus is "scheduled" or "checked_in" or "checked_out")
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture)
                .ToList()
            : [];
        return new ReceiptWorkQueue(
            ReadString(row, "queue_revision") ?? string.Empty,
            (int)(ReadLong(row, "pending_count") ?? 0),
            businessDay,
            workItem,
            buffer,
            ReadString(row, "resume_cursor"),
            advanceCastOptions);
    }

    private static IReadOnlyList<PendingReceiptItem> ParsePendingItems(IReadOnlyList<JsonElement> rows)
    {
        return rows.Select(item => new PendingReceiptItem
            {
                Id = ReadString(item, "document_id") ?? string.Empty,
                DocumentNo = ReadString(item, "document_id"),
                FileName = ReadString(item, "file_name"),
                FilePath = ReadString(item, "drive_url") ?? ReadString(item, "storage_path"),
                DriveFileId = ReadString(item, "drive_file_id"),
                PreviewUrl = BuildPreviewUrl(ReadString(item, "drive_file_id")),
                PaymentDate = ReadDateOnly(item, "document_date"),
                Amount = ReadDecimal(item, "amount"),
                WorkItemToken = ReadString(item, "work_item_token") ?? string.Empty
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToList();
    }

    private static StoreBusinessDay ParseBusinessDay(JsonElement row)
    {
        return new StoreBusinessDay
        {
            BusinessDayId = ReadLong(row, "business_day_id") ?? 0,
            CompanyId = ReadLong(row, "company_id") ?? 0,
            DepartmentId = ReadLong(row, "department_id") ?? 0,
            BusinessDate = ReadDateOnly(row, "business_date") ?? DateOnly.MinValue,
            OpenedAt = ReadDateTimeOffset(row, "opened_at") ?? DateTimeOffset.MinValue,
            ClosedAt = ReadDateTimeOffset(row, "closed_at"),
            Status = ReadString(row, "status") ?? string.Empty,
            Memo = ReadString(row, "memo"),
            BusinessUiRevision = ReadLong(row, "business_ui_revision") ?? 0
        };
    }

    private static BusinessDayClosingAttendanceItem ParseClosingAttendanceItem(JsonElement row)
    {
        var castId = ReadLong(row, "cast_id") ?? 0;
        var staffId = ReadLong(row, "staff_id") ?? 0;
        var personType = AttendancePersonTypes.Normalize(ReadString(row, "person_type") ??
            (staffId > 0 ? AttendancePersonTypes.Staff : AttendancePersonTypes.Cast));
        return new BusinessDayClosingAttendanceItem
        {
            AttendanceId = ReadLong(row, "attendance_id") ?? 0,
            PersonType = personType,
            PersonId = ReadLong(row, "person_id") ?? (personType == AttendancePersonTypes.Staff ? staffId : castId),
            CastId = castId,
            StaffId = staffId,
            DisplayName = ReadString(row, "display_name") ?? string.Empty,
            DepartmentName = ReadString(row, "department_name"),
            AttendanceStatus = ReadString(row, "attendance_status") ?? string.Empty,
            ClockInAt = ReadDateTimeOffset(row, "clock_in_at"),
            ClockOutAt = ReadDateTimeOffset(row, "clock_out_at"),
            UsesSendService = ReadBool(row, "uses_send_service") ?? false
        };
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "領収書の更新に失敗しました。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"領収書の更新に失敗しました。{rawError}";
    }

    private static string? BuildPreviewUrl(string? driveFileId)
    {
        if (string.IsNullOrWhiteSpace(driveFileId))
        {
            return null;
        }

        return $"/DrivePreview/{Uri.EscapeDataString(driveFileId)}";
    }
}
