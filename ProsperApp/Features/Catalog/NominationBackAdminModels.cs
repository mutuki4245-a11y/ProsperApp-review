using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Models;

public class NominationBackMasterItem
{
    public string NominationType { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string BackType { get; set; } = "nomination";

    public decimal BackUnitAmount { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class NominationBackMasterInputModel
{
    [Required(ErrorMessage = "指名種別を確認してください。")]
    public string NominationType { get; set; } = string.Empty;

    [Display(Name = "バック単価")]
    [Range(0, 9999999, ErrorMessage = "バック単価は0以上で入力してください。")]
    public decimal BackUnitAmount { get; set; }

    [Display(Name = "表示順")]
    [Range(0, 9999, ErrorMessage = "表示順は0から9999で入力してください。")]
    public int SortOrder { get; set; }

    [Display(Name = "有効")]
    public bool IsActive { get; set; } = true;
}

public class NominationBackMasterSaveResult
{
    public bool Succeeded { get; init; }

    public int UpdatedCount { get; init; }

    public string? ErrorMessage { get; init; }

    public static NominationBackMasterSaveResult Success(int updatedCount)
    {
        return new NominationBackMasterSaveResult { Succeeded = true, UpdatedCount = updatedCount };
    }

    public static NominationBackMasterSaveResult Failed(string message)
    {
        return new NominationBackMasterSaveResult { Succeeded = false, ErrorMessage = message };
    }
}
