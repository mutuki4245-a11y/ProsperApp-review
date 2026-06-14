using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseReceiptRepository(
    ISupabaseRpcClient rpcClient,
    IOptions<SupabaseOptions> options,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IReceiptRepository
{
    private readonly SupabaseOptions _options = options.Value;

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
            return SaveReceiptResult.Failed("Supabase SecretKeyが未設定です。領収書を更新できません。");
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
            requireSecretKey: true,
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
            return SaveReceiptResult.Failed("Supabase SecretKeyが未設定です。領収書を更新できません。");
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
            requireSecretKey: true,
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
