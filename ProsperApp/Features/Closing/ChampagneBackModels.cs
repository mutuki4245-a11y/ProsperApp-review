using System.ComponentModel.DataAnnotations;
using ProsperApp.Services;

namespace ProsperApp.Features.Closing;

public sealed class ChampagneBackStatus
{
    public int RequiredCastCount { get; init; }

    public int CompletedCastCount { get; init; }

    public int MissingCastCount { get; init; }

    public decimal TotalBackAmount { get; init; }

    public bool IsCompleted => MissingCastCount == 0;
}

public sealed class ChampagneBackOverview
{
    public ChampagneBackStatus Status { get; init; } = new();

    public IReadOnlyList<ChampagneBackCastRow> Casts { get; init; } = [];
}

public sealed class ChampagneBackCastRow
{
    public long CastId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? DepartmentName { get; init; }

    public decimal? BackAmount { get; init; }

    public bool IsSaved { get; init; }

    public string SearchDisplayName => string.IsNullOrWhiteSpace(DepartmentName)
        ? DisplayName
        : $"{DisplayName}：{DepartmentName}";

    public string BackAmountText => StoreUiText.Yen(BackAmount ?? 0);
}

public sealed class ChampagneBackSaveInput
{
    public long? BusinessDayId { get; set; }

    public List<ChampagneBackCastInput> Casts { get; set; } = [];
}

public sealed class ChampagneBackCastInput
{
    public long CastId { get; set; }

    [Display(Name = "シャンパン追加バック")]
    [Range(0, 999999999999, ErrorMessage = "シャンパン追加バックは0円以上の整数で入力してください。")]
    public decimal BackAmount { get; set; }
}

public sealed class ChampagneBackSaveResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public int SavedCastCount { get; init; }

    public decimal TotalBackAmount { get; init; }

    public static ChampagneBackSaveResult Success(int savedCastCount, decimal totalBackAmount)
    {
        return new ChampagneBackSaveResult
        {
            Succeeded = true,
            SavedCastCount = savedCastCount,
            TotalBackAmount = totalBackAmount
        };
    }

    public static ChampagneBackSaveResult Failed(string message)
    {
        return new ChampagneBackSaveResult { Succeeded = false, ErrorMessage = message };
    }
}
