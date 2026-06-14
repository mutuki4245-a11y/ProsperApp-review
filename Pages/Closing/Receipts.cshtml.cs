using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ReceiptsModel(
    IReceiptRepository receiptRepository,
    IDriveFileService driveFileService,
    IGoogleDriveAuthService googleDriveAuthService,
    IFeatureGate featureGate) : PageModel
{
    private readonly IReceiptRepository _receiptRepository = receiptRepository;
    private readonly IDriveFileService _driveFileService = driveFileService;
    private readonly IGoogleDriveAuthService _googleDriveAuthService = googleDriveAuthService;
    private readonly IFeatureGate _featureGate = featureGate;

    [BindProperty]
    public QuickEntryInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int Index { get; set; }

    public IReadOnlyList<PendingReceiptItem> PendingReceipts { get; private set; } = [];
    public PendingReceiptItem? CurrentReceipt { get; private set; }
    public string? NextPreviewUrl { get; private set; }
    public int CurrentPosition => PendingReceipts.Count == 0 ? 0 : CurrentIndex + 1;
    public int TotalCount => PendingReceipts.Count;
    public DateOnly PaymentDateMin => PaymentDateMax.AddYears(-1);
    public DateOnly PaymentDateMax => GetJapanToday();
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

    [TempData]
    public string? SuccessMessage { get; set; }

    public string? ErrorMessage { get; set; }

    public string? DriveAuthWarning { get; private set; }

    private int CurrentIndex { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!IsReceiptsEnabled())
        {
            return NotFound();
        }

        await LoadCurrentAsync(Index, cancellationToken);
        var authRedirect = await RedirectToGoogleLoginIfCurrentReceiptNeedsDriveAsync();
        if (authRedirect is not null)
        {
            return authRedirect;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostNextAsync(CancellationToken cancellationToken)
    {
        if (!IsReceiptsEnabled())
        {
            return NotFound();
        }

        NormalizeInput();

        if (string.IsNullOrWhiteSpace(Input.GroupCode))
        {
            Input.GroupCode = GenerateGroupCode();
        }

        await LoadCurrentAsync(Index, cancellationToken);
        ValidateQuickEntryInput();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _receiptRepository.SaveQuickEntryAsync(Input, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = "保存に失敗しました。時間をおいて再実行してください。";
            await LoadCurrentAsync(Index, cancellationToken);
            return Page();
        }

        SuccessMessage = "保存しました。";
        return RedirectToPage(new { index = Index });
    }

    public IActionResult OnPostSkip()
    {
        if (!IsReceiptsEnabled())
        {
            return NotFound();
        }

        return RedirectToPage(new { index = Index + 1 });
    }

    public async Task<IActionResult> OnPostDeleteScanMistakeAsync(CancellationToken cancellationToken)
    {
        if (!IsReceiptsEnabled())
        {
            return NotFound();
        }

        ModelState.Clear();

        if (string.IsNullOrWhiteSpace(Input.DocumentId) || string.IsNullOrWhiteSpace(Input.DriveFileId))
        {
            ErrorMessage = "証憑またはDriveファイルIDが取得できません。";
            await LoadCurrentAsync(Index, cancellationToken);
            return Page();
        }

        var configurationError = _googleDriveAuthService.ConfigurationErrorMessage;
        if (!string.IsNullOrWhiteSpace(configurationError))
        {
            ErrorMessage = configurationError;
            await LoadCurrentAsync(Index, cancellationToken);
            return Page();
        }

        if (!await _googleDriveAuthService.HasAccessTokenAsync())
        {
            return RedirectToPage(
                "/Login",
                new { returnUrl = BuildReceiptsReturnUrl(Index), forceGoogle = true });
        }

        var trashResult = await _driveFileService.TrashFileAsync(Input.DriveFileId, cancellationToken);
        if (!trashResult.Succeeded)
        {
            if (IsDriveAuthenticationFailure(trashResult.ErrorCode))
            {
                _googleDriveAuthService.ClearAccessToken();
                return RedirectToPage(
                    "/Login",
                    new { returnUrl = BuildReceiptsReturnUrl(Index), forceGoogle = true });
            }

            ErrorMessage = $"Driveファイルの削除に失敗しました。{trashResult.ErrorCode}: {trashResult.ErrorMessage}";
            await LoadCurrentAsync(Index, cancellationToken);
            return Page();
        }

        _driveFileService.RemoveCachedFile(Input.DriveFileId);

        var updateResult = await _receiptRepository.MarkScanMistakeAsync(Input.DocumentId, cancellationToken);
        if (!updateResult.Succeeded)
        {
            ErrorMessage = updateResult.ErrorMessage ?? "Driveファイルはゴミ箱へ移動しましたが、DBステータス更新に失敗しました。";
            await LoadCurrentAsync(Index, cancellationToken);
            return Page();
        }

        SuccessMessage = "スキャンミスとして削除しました。";
        return RedirectToPage(new { index = Index });
    }

    private async Task LoadCurrentAsync(int requestedIndex, CancellationToken cancellationToken)
    {
        PendingReceipts = await _receiptRepository.GetPendingAsync(cancellationToken);
        if (PendingReceipts.Count == 0)
        {
            CurrentReceipt = null;
            Input = new();
            CurrentIndex = 0;
            return;
        }

        CurrentIndex = Math.Clamp(requestedIndex, 0, PendingReceipts.Count - 1);
        CurrentReceipt = PendingReceipts[CurrentIndex];
        NextPreviewUrl = CurrentIndex + 1 < PendingReceipts.Count
            ? PendingReceipts[CurrentIndex + 1].PreviewUrl
            : null;
        if (string.IsNullOrWhiteSpace(Input.DocumentId))
        {
            Input = new QuickEntryInputModel
            {
                DocumentId = CurrentReceipt.Id,
                DriveFileId = CurrentReceipt.DriveFileId,
                PaymentDate = CurrentReceipt.PaymentDate ?? PaymentDateMax,
                Amount = CurrentReceipt.Amount
            };
        }
    }

    private async Task<IActionResult?> RedirectToGoogleLoginIfCurrentReceiptNeedsDriveAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentReceipt?.DriveFileId))
        {
            return null;
        }

        var configurationError = _googleDriveAuthService.ConfigurationErrorMessage;
        if (!string.IsNullOrWhiteSpace(configurationError))
        {
            DriveAuthWarning = configurationError;
            return null;
        }

        if (await _googleDriveAuthService.HasAccessTokenAsync())
        {
            return null;
        }

        return RedirectToPage("/Login", new { returnUrl = BuildReceiptsReturnUrl(CurrentIndex) });
    }

    private string BuildReceiptsReturnUrl(int index)
    {
        return Url.Page("/Closing/Receipts", new { index }) ?? $"/Closing/Receipts?index={index}";
    }

    private static string GenerateGroupCode()
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        return $"SP{now:yyMMdd}{suffix}";
    }

    private void NormalizeInput()
    {
        Input.DocumentId = Input.DocumentId?.Trim() ?? string.Empty;
        Input.DriveFileId = Input.DriveFileId?.Trim();
        Input.AccountSubject = Input.AccountSubject?.Trim() ?? string.Empty;
        Input.Description = Input.Description?.Trim() ?? string.Empty;
        Input.GroupCode = Input.GroupCode?.Trim();
    }

    private void ValidateQuickEntryInput()
    {
        if (CurrentReceipt is null)
        {
            ModelState.AddModelError(string.Empty, "保存対象の証憑が見つかりません。");
            return;
        }

        if (!string.Equals(Input.DocumentId, CurrentReceipt.Id, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "表示中の証憑と送信された証憑が一致しません。画面を再読み込みしてください。");
        }

        var expectedDriveFileId = CurrentReceipt.DriveFileId ?? string.Empty;
        var postedDriveFileId = Input.DriveFileId ?? string.Empty;
        if (!string.Equals(postedDriveFileId, expectedDriveFileId, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "表示中のDriveファイルと送信されたDriveファイルが一致しません。画面を再読み込みしてください。");
        }

        if (Input.PaymentDate is { } paymentDate)
        {
            if (paymentDate < PaymentDateMin || paymentDate > PaymentDateMax)
            {
                ModelState.AddModelError("Input.PaymentDate", $"支払日は{PaymentDateMin:yyyy/MM/dd}から{PaymentDateMax:yyyy/MM/dd}までの日付を入力してください。");
            }
        }

        if (Input.Amount is { } amount && decimal.Truncate(amount) != amount)
        {
            ModelState.AddModelError("Input.Amount", "金額は小数なしの整数円で入力してください。");
        }

        if (!string.IsNullOrWhiteSpace(Input.AccountSubject) && !AllowedAccountSubjects.Contains(Input.AccountSubject))
        {
            ModelState.AddModelError("Input.AccountSubject", "科目は画面の一覧から選択してください。");
        }

        if (string.IsNullOrWhiteSpace(Input.Description))
        {
            ModelState.AddModelError("Input.Description", "摘要を入力してください。");
        }
    }

    private HashSet<string> AllowedAccountSubjects =>
        AccountSubjectGroups
            .SelectMany(group => group.Items.Select(item => $"{group.Name}: {item}"))
            .ToHashSet(StringComparer.Ordinal);

    private bool IsReceiptsEnabled()
    {
        return _featureGate.IsEnabled(FeatureNames.Closing) &&
               _featureGate.IsEnabled(FeatureNames.Receipts);
    }

    private static DateOnly GetJapanToday()
    {
        var timeZone = GetJapanTimeZone();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));
    }

    private static TimeZoneInfo GetJapanTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
    }

    private static bool IsDriveAuthenticationFailure(string? errorCode)
    {
        return errorCode is "missing_access_token" ||
               errorCode?.EndsWith("_401", StringComparison.OrdinalIgnoreCase) == true;
    }

    public sealed record AccountSubjectGroup(string Name, IReadOnlyList<string> Items);
}


