using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Models;

public class QuickEntryInputModel
{
    [Required]
    public string DocumentId { get; set; } = string.Empty;

    public string? DriveFileId { get; set; }

    [Display(Name = "支払日")]
    [Required(ErrorMessage = "支払日を入力してください。")]
    public DateOnly? PaymentDate { get; set; }

    [Display(Name = "金額")]
    [Required(ErrorMessage = "金額を入力してください。")]
    [Range(typeof(decimal), "1", "9999999", ErrorMessage = "金額は1円以上9,999,999円以下で入力してください。")]
    public decimal? Amount { get; set; }

    [Display(Name = "科目")]
    [Required(ErrorMessage = "科目を選択してください。")]
    [StringLength(100, ErrorMessage = "科目は100文字以内で入力してください。")]
    public string AccountSubject { get; set; } = string.Empty;

    [Display(Name = "摘要")]
    [Required(ErrorMessage = "摘要を入力してください。")]
    [StringLength(500, ErrorMessage = "摘要は500文字以内で入力してください。")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "グループコード")]
    [StringLength(32, ErrorMessage = "グループコードは32文字以内で入力してください。")]
    public string? GroupCode { get; set; }
}
