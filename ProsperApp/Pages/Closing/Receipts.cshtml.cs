using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ReceiptsModel(
    IReceiptRepository receiptRepository,
    IFeatureGate featureGate,
    IStoreClock storeClock) : PageModel
{
    private readonly IReceiptRepository _receiptRepository = receiptRepository;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IStoreClock _storeClock = storeClock;
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    [BindProperty]
    public QuickEntryInputModel Input { get; set; } = new();

    public DateOnly PaymentDateMin => PaymentDateMax.AddYears(-1);
    public DateOnly PaymentDateMax => _storeClock.GetStoreToday();

    public IReadOnlyList<AccountSubjectGroup> AccountSubjectGroups { get; } =
    [
        new("前渡金", ["スタッフ", "キャスト"]),
        new("雑給", ["スタッフ日払い/体験入店"]),
        new("外注費", ["キャスト日払い/体験入店", "送りドライバー", "ヘアメイク"]),
        new("水道光熱費", ["水道", "電気", "ガス"]),
        new("通信費", ["電話", "ネット"]),
        new("リース費", ["カラオケ", "おしぼり", "ダーツマシン"]),
        new("衛生費", ["ごみ処理", "ダスキン", "害虫駆除"]),
        new("宣伝広告費", ["名刺/看板/ポスター", "募集", "案内所"]),
        new("仕入れ", ["食材", "酒", "出前"]),
        new("消耗品費", ["事務用品", "衛生用品"]),
        new("保険料", ["自動車保険", "店舗火災保険"]),
        new("旅費交通費", ["タクシー/電車代", "ガソリン"]),
        new("地代家賃", ["駐車場", "店舗家賃"]),
        new("租税公課", ["収入印紙/自動車税", "行政手数料"]),
        new("飲食代", ["食事(一人)", "会議費(複数人)", "交際費(キャスト含む複数人)"]),
        new("雑費", ["コピー機", "クリーニング"]),
        new("その他", ["福利厚生費", "著作権利用料", "雑費"])
    ];

    public IActionResult OnGet()
    {
        if (!IsReceiptsEnabled())
        {
            return NotFound();
        }

        Input.PaymentDate = _storeClock.GetCurrentBusinessDate();
        return Page();
    }

    public async Task<IActionResult> OnGetWorkQueueAsync(string? resumeCursor, CancellationToken cancellationToken)
    {
        if (!IsReceiptsEnabled())
        {
            return NotFound();
        }

        var result = await _receiptRepository.GetCurrentWorkQueueAsync(resumeCursor, cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                message = result.ErrorMessage ?? "領収書作業キューを取得できませんでした。"
            });
        }

        return new JsonResult(ToQueuePayload(result.Value));
    }

    public async Task<IActionResult> OnPostQueueAdvanceAsync(CancellationToken cancellationToken)
    {
        if (!IsReceiptsEnabled())
        {
            return NotFound();
        }

        if (!Request.HasJsonContentType())
        {
            return BadRequest(new { succeeded = false, status = "validation_error", message = "送信内容を確認してください。" });
        }

        ReceiptWorkQueueAdvanceInput? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<ReceiptWorkQueueAdvanceInput>(
                Request.Body,
                RequestJsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            input = null;
        }

        if (input is null ||
            !Guid.TryParse(input.OperationId, out _) ||
            input.Action is not ("save" or "exclude_scan_mistake") ||
            string.IsNullOrWhiteSpace(input.DocumentId) ||
            string.IsNullOrWhiteSpace(input.WorkItemToken) ||
            (input.Action == "save" && !AllowedAccountSubjects.Contains(input.AccountSubject?.Trim() ?? string.Empty)))
        {
            return BadRequest(new { succeeded = false, status = "validation_error", message = "送信内容を確認してください。" });
        }

        var result = await _receiptRepository.AdvanceWorkQueueAsync(input, cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                status = "unavailable",
                message = result.ErrorMessage ?? "領収書を保存できませんでした。"
            });
        }

        var output = result.Value;
        var queue = output.Queue;
        return new JsonResult(new
        {
            succeeded = true,
            status = output.Status,
            documentId = output.DocumentId,
            message = output.Message,
            queueRevision = queue.Revision,
            pendingCount = queue.PendingCount,
            businessDay = queue.BusinessDay,
            workItem = ToClientItem(queue.WorkItem),
            buffer = queue.Buffer.Select(ToClientItem),
            resumeCursor = queue.ResumeCursor,
            advanceCasts = queue.AdvanceCastOptions.Select(item => new { id = item.CastId, name = item.SearchDisplayName }),
            pendingReceiptCount = output.PendingReceiptCount
        });
    }

    private object ToQueuePayload(ReceiptWorkQueue queue) => new
    {
        succeeded = true,
        queueRevision = queue.Revision,
        pendingCount = queue.PendingCount,
        businessDay = queue.BusinessDay,
        workItem = ToClientItem(queue.WorkItem),
        buffer = queue.Buffer.Select(ToClientItem),
        resumeCursor = queue.ResumeCursor,
        advanceCasts = queue.AdvanceCastOptions.Select(item => new
        {
            id = item.CastId,
            name = item.SearchDisplayName
        })
    };

    private static object? ToClientItem(PendingReceiptItem? item) => item is null ? null : new
    {
        id = item.Id,
        fileName = item.FileName,
        driveFileId = item.DriveFileId,
        previewUrl = item.PreviewUrl,
        paymentDate = item.PaymentDate?.ToString("yyyy-MM-dd"),
        amount = item.Amount,
        workItemToken = item.WorkItemToken
    };

    private HashSet<string> AllowedAccountSubjects => AccountSubjectGroups
        .SelectMany(group => group.Items.Select(item => $"{group.Name}: {item}"))
        .ToHashSet(StringComparer.Ordinal);

    private bool IsReceiptsEnabled() =>
        _featureGate.IsEnabled(FeatureNames.Closing) &&
        _featureGate.IsEnabled(FeatureNames.Receipts);

    public sealed record AccountSubjectGroup(string Name, IReadOnlyList<string> Items);
}
