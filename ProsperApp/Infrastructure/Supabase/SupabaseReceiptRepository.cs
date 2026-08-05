using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Features.Shared;
using ProsperApp.Features.Slips;
using ProsperApp.Infrastructure.Caching;
using ProsperApp.Options;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseReceiptRepository(
    ISupabaseRpcClient rpcClient,
    IOptions<SupabaseOptions> options,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IReceiptRepository
{
    private static readonly TimeSpan PendingCacheDuration = TimeSpan.FromSeconds(30);
    private readonly SupabaseOptions _options = options.Value;
    private readonly IApplicationCache _cache = cache;

    public async Task<Result<IReadOnlyList<PendingReceiptItem>>> GetPendingResultAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<PendingReceiptItem>>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。未処理領収書を取得できません。");
        }

        var cacheKey = BuildPendingCacheKey();
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<PendingReceiptItem>? cached) &&
            cached is not null)
        {
            return Result<IReadOnlyList<PendingReceiptItem>>.Success(cached);
        }

        var rpcResult = await RpcClient.PostArrayAsync(
            "store.get_pending_receipts",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_status = _options.PendingStatus
            },
            ct);
        if (!rpcResult.Succeeded)
        {
            var failure = RpcFailure<IReadOnlyList<PendingReceiptItem>>(
                rpcResult.ErrorMessage,
                "未処理領収書を取得できませんでした。");
            return Result<IReadOnlyList<PendingReceiptItem>>.Failure(
                failure.FailureKind ?? ResultFailureKind.Unavailable,
                ToFriendlyError(failure.ErrorMessage));
        }

        var pending = ParsePendingItems(rpcResult.Rows);
        _cache.Set(cacheKey, pending, PendingCacheDuration, "営業中", "未処理領収書");
        return Result<IReadOnlyList<PendingReceiptItem>>.Success(pending);
    }

    public async Task<Result<ReceiptWorkQueue>> GetCurrentWorkQueueAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<ReceiptWorkQueue>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。領収書作業キューを取得できません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_current_receipt_work_queue",
            new { p_department_id = CurrentStoreDepartmentId },
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
        var items = row.TryGetProperty("receipts", out var receiptsJson) &&
                    receiptsJson.ValueKind == JsonValueKind.Array
            ? ParsePendingItems(receiptsJson.EnumerateArray().ToArray())
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
            businessDay,
            items,
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

        var output = new ReceiptWorkQueueAdvanceResult(
            ReadString(response, "status") ?? "unavailable",
            ReadString(response, "document_id"),
            ReadString(response, "message"));
        return Result<ReceiptWorkQueueAdvanceResult>.Success(output);
    }

    public async Task<Result<bool>> IsPendingDriveFileAllowedAsync(string driveFileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(driveFileId))
        {
            return Result<bool>.Failure(ResultFailureKind.InvalidInput, "DriveファイルIDがありません。");
        }

        var pending = await GetPendingResultAsync(ct);
        return pending.Succeeded
            ? Result<bool>.Success(pending.Value.Any(x => x.DriveFileId == driveFileId))
            : Result<bool>.Failure(
                pending.FailureKind ?? ResultFailureKind.Unavailable,
                pending.ErrorMessage ?? "未処理領収書を確認できませんでした。");
    }

    public async Task<SaveReceiptResult> SaveQuickEntryAsync(QuickEntryInputModel input, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return SaveReceiptResult.Failed("Supabase Edge Function設定が未設定です。領収書を更新できません。");
        }

        if (string.IsNullOrWhiteSpace(input.DocumentId) ||
            input.PaymentDate is null ||
            input.Amount is not > 0 ||
            string.IsNullOrWhiteSpace(input.AccountSubject) ||
            string.IsNullOrWhiteSpace(input.Description))
        {
            return SaveReceiptResult.Failed("領収書保存に必要な入力が不足しています。");
        }

        var companyIdResult = await GetCompanyIdAsync(ct);
        if (!companyIdResult.Succeeded || companyIdResult.Value is null)
        {
            return SaveReceiptResult.Failed(
                companyIdResult.ErrorMessage ??
                "店舗の会社IDを取得できません。店舗設定とstore.get_context RPCを確認してください。");
        }

        var journalPayload = BuildJournalPayload(input, companyIdResult.Value.Value, CurrentStoreDepartmentId);

        var result = await RpcClient.PostArrayAsync(
            "store.quick_enter_receipt",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_document_id = input.DocumentId,
                p_payment_date = input.PaymentDate,
                p_amount = input.Amount,
                p_account_subject = input.AccountSubject.Trim(),
                p_description = input.Description.Trim(),
                p_group_code = input.GroupCode,
                p_journal_payload = journalPayload,
                p_status = _options.CompletedStatus,
                p_business_day_id = input.BusinessDayId,
                p_advance_cast_id = input.AdvanceCastId
            },
            ct);

        if (!result.Succeeded)
        {
            return SaveReceiptResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return SaveReceiptResult.Failed("対象の領収書を更新できません。店舗設定またはステータスを確認してください。");
        }

        ClearPendingCache();
        return SaveReceiptResult.Success(input.DocumentId);
    }

    public async Task<SaveReceiptResult> MarkScanMistakeAsync(string documentId, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return SaveReceiptResult.Failed("Supabase Edge Function設定が未設定です。領収書を更新できません。");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return SaveReceiptResult.Failed("DocumentId is required.");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.mark_receipt_scan_mistake",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_document_id = documentId,
                p_status = _options.ScanMistakeStatus
            },
            ct);

        if (!result.Succeeded)
        {
            return SaveReceiptResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return SaveReceiptResult.Failed("対象の領収書を更新できません。店舗設定またはステータスを確認してください。");
        }

        ClearPendingCache();
        return SaveReceiptResult.Success(documentId);
    }

    private string BuildPendingCacheKey() =>
        $"receipt-pending:{CurrentStoreDepartmentId}:{_options.PendingStatus}";

    private void ClearPendingCache()
    {
        _cache.Remove(BuildPendingCacheKey());
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

    private async Task<Result<long?>> GetCompanyIdAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(StoreMasterCacheKeys.StoreContext(CurrentStoreDepartmentId), out StoreContext? cachedContext) &&
            cachedContext?.CompanyId > 0)
        {
            return Result<long?>.Success(cachedContext.CompanyId);
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_context",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);
        if (!result.Succeeded)
        {
            return Result<long?>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "店舗の会社IDを取得できませんでした。");
        }

        if (result.Value.Count == 0)
        {
            return Result<long?>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗マスタを取得できません。管理者設定で利用店舗を確認してください。");
        }

        var companyId = ReadLong(result.Value[0], "company_id");
        return companyId is > 0
            ? Result<long?>.Success(companyId)
            : Result<long?>.Failure(
                ResultFailureKind.InvalidResponse,
                "店舗の会社IDが正しくありません。");
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
            Memo = ReadString(row, "memo")
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

    private static DocumentJournalSavePayload BuildJournalPayload(
        QuickEntryInputModel input,
        long companyId,
        long departmentId)
    {
        var documentId = input.DocumentId.Trim();
        var amount = input.Amount ?? 0;
        var memo = BuildJournalMemo(input.Description, input.GroupCode);
        var journalEntryId = BuildJournalEntryId(documentId);

        var payload = new DocumentJournalSavePayload();
        payload.JournalEntries.Add(new DocumentJournalEntryRecord
        {
            JournalEntryId = journalEntryId,
            JournalDate = input.PaymentDate ?? throw new InvalidOperationException("Payment date is required."),
            Status = "confirmed"
        });
        payload.JournalEntryLines.Add(new DocumentJournalEntryLineRecord
        {
            JournalEntryId = journalEntryId,
            LineNo = 1,
            Side = "debit",
            AccountCode = ExtractDebitAccountCode(input.AccountSubject),
            CompanyId = companyId,
            DepartmentId = departmentId,
            IsReducedTaxRate = false,
            LineMemo = memo,
            Amount = amount
        });
        payload.JournalEntryLines.Add(new DocumentJournalEntryLineRecord
        {
            JournalEntryId = journalEntryId,
            LineNo = 2,
            Side = "credit",
            AccountCode = "現金",
            CompanyId = companyId,
            DepartmentId = null,
            IsReducedTaxRate = false,
            LineMemo = memo,
            Amount = amount
        });
        payload.DocumentJournalLinks.Add(new DocumentJournalLinkRecord
        {
            JournalEntryId = journalEntryId,
            DocumentId = documentId
        });

        return payload;
    }

    private static string BuildJournalEntryId(string documentId)
    {
        return ReceiptJournalEntryId.Create(documentId);
    }

    private static string BuildJournalMemo(string description, string? groupCode)
    {
        var memo = description.Trim();
        var normalizedGroupCode = groupCode?.Trim();
        return string.IsNullOrWhiteSpace(normalizedGroupCode)
            ? memo
            : $"{memo} [G:{normalizedGroupCode}]";
    }

    private static string ExtractDebitAccountCode(string accountSubject)
    {
        var normalized = accountSubject.Trim();
        var separatorIndex = normalized.IndexOf(':');
        if (separatorIndex < 0)
        {
            separatorIndex = normalized.IndexOf('：');
        }

        return separatorIndex > 0
            ? normalized[..separatorIndex].Trim()
            : normalized;
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
