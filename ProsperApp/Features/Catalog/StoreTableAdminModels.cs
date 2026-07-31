using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Features.Catalog;

public sealed class StoreTableAdminItem
{
    public long TableId { get; set; }

    public string TableCode { get; set; } = string.Empty;

    public string? TableName { get; set; }

    public int TableCategoryNo { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }
}

public sealed class StoreTableInputModel
{
    public long? TableId { get; set; }

    [Display(Name = "卓番コード")]
    [Required(ErrorMessage = "卓番コードを入力してください。")]
    [StringLength(40, ErrorMessage = "卓番コードは40文字以内で入力してください。")]
    public string TableCode { get; set; } = string.Empty;

    [Display(Name = "表示名")]
    [StringLength(100, ErrorMessage = "表示名は100文字以内で入力してください。")]
    public string? TableName { get; set; }

    [Display(Name = "区分")]
    [Range(0, 9, ErrorMessage = "区分は0から9で入力してください。")]
    public int TableCategoryNo { get; set; }

    [Display(Name = "表示順")]
    [Range(0, 9999, ErrorMessage = "表示順は0から9999で入力してください。")]
    public int SortOrder { get; set; }

    [Display(Name = "有効")]
    public bool IsActive { get; set; } = true;
}

public sealed class StoreTableAdminSaveResult
{
    public bool Succeeded { get; init; }

    public long? TableId { get; init; }

    public string? ErrorMessage { get; init; }

    public static StoreTableAdminSaveResult Success(long tableId) =>
        new() { Succeeded = true, TableId = tableId };

    public static StoreTableAdminSaveResult Failed(string message) =>
        new() { Succeeded = false, ErrorMessage = message };
}
