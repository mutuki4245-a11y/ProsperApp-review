using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseReceiptRepository(
    ISupabaseRpcClient rpcClient,
    IOptions<SupabaseOptions> options,
    IDocumentApiClient documentApiClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IReceiptRepository
{
    private readonly SupabaseOptions _options = options.Value;
    private readonly IDocumentApiClient _documentApiClient = documentApiClient;

    public async Task<IReadOnlyList<PendingReceiptItem>> GetPendingAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_pending_receipts",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_status = _options.PendingStatus
            },
            ct);

        return ParsePendingItems(rows);
    }

    public async Task<bool> IsPendingDriveFileAllowedAsync(string driveFileId, CancellationToken ct)
    {
        if (!HasRequiredSettings() || string.IsNullOrWhiteSpace(driveFileId))
        {
            return false;
        }

        var pending = await GetPendingAsync(ct);
        return pending.Any(x => x.DriveFileId == driveFileId);
    }

    public async Task<SaveReceiptResult> SaveQuickEntryAsync(QuickEntryInputModel input, CancellationToken ct)
    {
        if (!HasMutationSettings())
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

        var companyId = await GetCompanyIdAsync(ct);
        if (companyId is null)
        {
            return SaveReceiptResult.Failed("店舗の会社IDを取得できません。店舗設定とget_store_context RPCを確認してください。");
        }

        var payload = BuildJournalPayload(input, companyId.Value, CurrentStoreDepartmentId);
        var documentApiResult = await _documentApiClient.SaveJournalPayloadAsync(payload, ct);
        if (!documentApiResult.Succeeded)
        {
            return SaveReceiptResult.Failed(documentApiResult.ErrorMessage ?? "DocManagement連携保存に失敗しました。");
        }

        var result = await RpcClient.PostArrayAsync(
            "quick_enter_receipt",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_document_id = input.DocumentId,
                p_payment_date = input.PaymentDate,
                p_amount = input.Amount,
                p_account_subject = input.AccountSubject.Trim(),
                p_description = input.Description.Trim(),
                p_group_code = input.GroupCode,
                p_status = _options.CompletedStatus
            },
            ct);

        if (!result.Succeeded)
        {
            return SaveReceiptResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return result.Rows.Count == 0
            ? SaveReceiptResult.Failed("対象の領収書を更新できません。店舗設定またはステータスを確認してください。")
            : SaveReceiptResult.Success(input.DocumentId);
    }

    public async Task<SaveReceiptResult> MarkScanMistakeAsync(string documentId, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SaveReceiptResult.Failed("Supabase Edge Function設定が未設定です。領収書を更新できません。");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return SaveReceiptResult.Failed("DocumentId is required.");
        }

        var result = await RpcClient.PostArrayAsync(
            "mark_receipt_scan_mistake",
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

        return result.Rows.Count == 0
            ? SaveReceiptResult.Failed("対象の領収書を更新できません。店舗設定またはステータスを確認してください。")
            : SaveReceiptResult.Success(documentId);
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
                Amount = ReadDecimal(item, "amount")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToList();
    }

    private async Task<long?> GetCompanyIdAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_context",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        if (rows.Count == 0)
        {
            return null;
        }

        var companyId = ReadLong(rows[0], "company_id");
        return companyId is > 0 ? companyId : null;
    }

    private static DocumentJournalSavePayload BuildJournalPayload(
        QuickEntryInputModel input,
        long companyId,
        long departmentId)
    {
        var documentId = input.DocumentId.Trim();
        var amount = input.Amount ?? 0;
        var memo = input.Description.Trim();
        var journalEntryId = BuildJournalEntryId(documentId);

        var payload = new DocumentJournalSavePayload();
        payload.JournalEntries.Add(new DocumentJournalEntryRecord
        {
            JournalEntryId = journalEntryId,
            JournalDate = input.PaymentDate ?? DateOnly.FromDateTime(DateTime.Today),
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
        return $"prosper-receipt-{documentId}";
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
