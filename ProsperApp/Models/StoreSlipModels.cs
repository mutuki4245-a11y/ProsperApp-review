using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Models;

public class StoreContext
{
    public long CompanyId { get; set; }
    public long DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
}

public class StoreTableOption
{
    public long TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string? TableName { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(TableName)
        ? TableCode
        : $"{TableCode} {TableName}";
}

public class CastOption
{
    public long CastId { get; set; }
    public string? CastCode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public class CreateSlipInputModel
{
    [Display(Name = "卓番")]
    [Required(ErrorMessage = "卓番を選択してください。")]
    public long? TableId { get; set; }

    [Display(Name = "営業日")]
    public DateOnly? BusinessDate { get; set; }

    public long? BusinessDayId { get; set; }

    [Display(Name = "入店時刻")]
    [Required(ErrorMessage = "入店時刻を選択してください。")]
    public string? OpenedTime { get; set; }

    public DateTime? OpenedAt { get; set; }

    [Display(Name = "客数")]
    public int CustomerCount => CustomerLabels.Count;

    [Display(Name = "客情報")]
    public List<string?> CustomerLabels { get; set; } = [null];

    [Display(Name = "指名キャスト")]
    public List<long> CastIds { get; set; } = [];

    [Display(Name = "メモ")]
    [StringLength(500, ErrorMessage = "メモは500文字以内で入力してください。")]
    public string? Memo { get; set; }
}

public class CreateSlipResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public long? SlipId { get; init; }

    public static CreateSlipResult Success(long slipId)
    {
        return new CreateSlipResult { Succeeded = true, SlipId = slipId };
    }

    public static CreateSlipResult Failed(string message)
    {
        return new CreateSlipResult { Succeeded = false, ErrorMessage = message };
    }
}

public class BusinessSlipListItem
{
    public long SlipId { get; set; }
    public string? SlipNo { get; set; }
    public long? TableId { get; set; }
    public string? TableCode { get; set; }
    public string? TableName { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CustomerCount { get; set; }
    public string? Memo { get; set; }

    public string TableDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(TableCode) && !string.IsNullOrWhiteSpace(TableName))
            {
                return $"{TableCode} {TableName}";
            }

            return TableCode ?? TableName ?? "-";
        }
    }
}

public class StoreBusinessDay
{
    public long BusinessDayId { get; set; }
    public long CompanyId { get; set; }
    public long DepartmentId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Memo { get; set; }
}

public class BusinessDayOperationResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public StoreBusinessDay? BusinessDay { get; init; }

    public static BusinessDayOperationResult Success(StoreBusinessDay businessDay)
    {
        return new BusinessDayOperationResult { Succeeded = true, BusinessDay = businessDay };
    }

    public static BusinessDayOperationResult Failed(string message)
    {
        return new BusinessDayOperationResult { Succeeded = false, ErrorMessage = message };
    }
}
