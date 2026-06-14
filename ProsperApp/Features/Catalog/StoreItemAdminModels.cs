using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Models;

public class StoreItemAdminCatalog
{
    public IReadOnlyList<StoreItemCategoryAdminItem> Categories { get; init; } = [];

    public IReadOnlyList<StoreItemAdminItem> Items { get; init; } = [];
}

public class StoreItemCategoryAdminItem
{
    public long ItemCategoryId { get; set; }

    public string CategoryCode { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }
}

public class StoreItemAdminItem
{
    public long ItemId { get; set; }

    public long? ItemCategoryId { get; set; }

    public string CategoryCode { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public decimal DefaultPrice { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public bool IsCastBackTarget { get; set; }

    public decimal CastBackRegularUnitAmount { get; set; }

    public decimal CastBackNominationUnitAmount { get; set; }

    public string CastBackType { get; set; } = "drink";
}

public class StoreItemOrderInputModel
{
    [Range(1, long.MaxValue, ErrorMessage = "商品を選択してください。")]
    public long ItemId { get; set; }

    [Range(0, 999999, ErrorMessage = "並び順を確認してください。")]
    public int SortOrder { get; set; }
}

public class StoreItemCategoryInputModel
{
    public long? ItemCategoryId { get; set; }

    [Display(Name = "カテゴリコード")]
    [Required(ErrorMessage = "カテゴリコードを入力してください。")]
    [StringLength(40, ErrorMessage = "カテゴリコードは40文字以内で入力してください。")]
    public string CategoryCode { get; set; } = string.Empty;

    [Display(Name = "カテゴリ名")]
    [Required(ErrorMessage = "カテゴリ名を入力してください。")]
    [StringLength(100, ErrorMessage = "カテゴリ名は100文字以内で入力してください。")]
    public string CategoryName { get; set; } = string.Empty;

    [Display(Name = "表示順")]
    [Range(0, 9999, ErrorMessage = "表示順は0から9999で入力してください。")]
    public int SortOrder { get; set; }

    [Display(Name = "有効")]
    public bool IsActive { get; set; } = true;
}

public class StoreItemInputModel
{
    public long? ItemId { get; set; }

    [Display(Name = "カテゴリ")]
    [Required(ErrorMessage = "カテゴリを選択してください。")]
    public long? ItemCategoryId { get; set; }

    [Display(Name = "商品名")]
    [Required(ErrorMessage = "商品名を入力してください。")]
    [StringLength(120, ErrorMessage = "商品名は120文字以内で入力してください。")]
    public string ItemName { get; set; } = string.Empty;

    [Display(Name = "税込価格")]
    [Range(0, 9999999, ErrorMessage = "税込価格は0以上で入力してください。")]
    public decimal DefaultPrice { get; set; }

    [Display(Name = "有効")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "バック対象")]
    public bool IsCastBackTarget { get; set; }

    [Display(Name = "通常バック単価")]
    [Range(0, 9999999, ErrorMessage = "通常バック単価は0以上で入力してください。")]
    public decimal CastBackRegularUnitAmount { get; set; }

    [Display(Name = "指名バック単価")]
    [Range(0, 9999999, ErrorMessage = "指名バック単価は0以上で入力してください。")]
    public decimal CastBackNominationUnitAmount { get; set; }
}

public class StoreItemAdminSaveResult
{
    public bool Succeeded { get; init; }

    public long? Id { get; init; }

    public string? ErrorMessage { get; init; }

    public static StoreItemAdminSaveResult Success(long id)
    {
        return new StoreItemAdminSaveResult { Succeeded = true, Id = id };
    }

    public static StoreItemAdminSaveResult Failed(string message)
    {
        return new StoreItemAdminSaveResult { Succeeded = false, ErrorMessage = message };
    }
}
