using System.ComponentModel.DataAnnotations;
using ProsperApp.Services;

namespace ProsperApp.Models;

public class StoreContext
{
    public long CompanyId { get; set; }
    public long DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int AttendanceMinuteStep { get; set; } = 15;
    public string CastSalesAmountBasis { get; set; } = LocalSettings.CastSalesAmountBasisTotal;
    public string CastSalesSplitMode { get; set; } = LocalSettings.CastSalesSplitModeSplit;
}

public class StoreTableOption
{
    public long TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string? TableName { get; set; }
    public int TableCategoryNo { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(TableName)
        ? TableCode
        : $"{TableCode} {TableName}";
}

public class CastOption
{
    public long CastId { get; set; }
    public string? CastCode { get; set; }
    public string? DepartmentName { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public string SearchDisplayName => string.IsNullOrWhiteSpace(DepartmentName)
        ? DisplayName
        : $"{DisplayName}：{DepartmentName}";
}

public class CastOptionsLoadResult
{
    public bool Succeeded { get; init; }

    public IReadOnlyList<CastOption> Casts { get; init; } = [];

    public string? ErrorMessage { get; init; }

    public static CastOptionsLoadResult Success(IReadOnlyList<CastOption> casts)
    {
        return new CastOptionsLoadResult { Succeeded = true, Casts = casts };
    }

    public static CastOptionsLoadResult Failed(string message)
    {
        return new CastOptionsLoadResult { Succeeded = false, ErrorMessage = message };
    }
}

public class StoreCastAdminItem
{
    public long CastId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DrinkMemo { get; set; }
    public DateOnly JoinedOn { get; set; }
}

public class StoreCastCreateInputModel
{
    [Display(Name = "キャスト名")]
    [Required(ErrorMessage = "キャスト名を入力してください。")]
    [StringLength(120, ErrorMessage = "キャスト名は120文字以内で入力してください。")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "ドリンクメモ")]
    [StringLength(300, ErrorMessage = "ドリンクメモは300文字以内で入力してください。")]
    public string? DrinkMemo { get; set; }
}

public class StoreCastDrinkMemoInputModel
{
    [Range(1, long.MaxValue, ErrorMessage = "編集するキャストを選択してください。")]
    public long CastId { get; set; }

    [Display(Name = "ドリンクメモ")]
    [StringLength(300, ErrorMessage = "ドリンクメモは300文字以内で入力してください。")]
    public string? DrinkMemo { get; set; }
}

public class StoreCastSaveResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public long? CastId { get; init; }

    public static StoreCastSaveResult Success(long castId)
    {
        return new StoreCastSaveResult { Succeeded = true, CastId = castId };
    }

    public static StoreCastSaveResult Failed(string message)
    {
        return new StoreCastSaveResult { Succeeded = false, ErrorMessage = message };
    }
}

public class CastNominationInputModel
{
    public string? NominationKind { get; set; }

    [Range(1000, 20000, ErrorMessage = "指名料金は1000円から20000円で選択してください。")]
    public decimal NominationPrice { get; set; } = 1000;

    public long? CastId { get; set; }

    [StringLength(160, ErrorMessage = "キャスト名は160文字以内で入力してください。")]
    public string? CastName { get; set; }
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

    [Display(Name = "指名情報")]
    public List<CastNominationInputModel> CastNominations { get; set; } = [];

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

public class SlipMutationResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public int AffectedCount { get; init; }

    public static SlipMutationResult Success(int affectedCount)
    {
        return new SlipMutationResult { Succeeded = true, AffectedCount = affectedCount };
    }

    public static SlipMutationResult Failed(string message)
    {
        return new SlipMutationResult { Succeeded = false, ErrorMessage = message };
    }
}

public class AddSlipCustomersInputModel
{
    [Display(Name = "客情報")]
    public List<string?> CustomerLabels { get; set; } = [null];

    [Display(Name = "入店時刻")]
    public string? EnteredTime { get; set; }

    public DateTime? EnteredAt { get; set; }
}

public class AddSlipNominationsInputModel
{
    [Display(Name = "指名情報")]
    public List<CastNominationInputModel> CastNominations { get; set; } = [];
}

public class LeaveSlipCustomerInputModel
{
    [Display(Name = "退店する客")]
    [Required(ErrorMessage = "退店する客を選択してください。")]
    public long? SlipCustomerId { get; set; }

    [Display(Name = "退店時刻")]
    [Required(ErrorMessage = "退店時刻を選択してください。")]
    public string? LeftTime { get; set; }

    public DateTime? LeftAt { get; set; }
}

public class UpdateSlipCustomerInputModel
{
    [Required(ErrorMessage = "客を選択してください。")]
    public long? SlipCustomerId { get; set; }

    [StringLength(100, ErrorMessage = "客名は100文字以内で入力してください。")]
    public string? CustomerLabel { get; set; }
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
    public string? CustomerNames { get; set; }
    public string? CastNames { get; set; }
    public int OrderCount { get; set; }
    public decimal OrderSubtotalAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal AccountingAmount { get; set; }
    public decimal KaraokeQuantity { get; set; }
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

public class SlipDetail
{
    public long SlipId { get; set; }
    public string? SlipNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public long? TableId { get; set; }
    public string? TableCode { get; set; }
    public string? TableName { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CustomerCount { get; set; }
    public string? Memo { get; set; }
    public List<SlipDetailCustomer> Customers { get; } = [];
    public List<SlipDetailNomination> Nominations { get; } = [];
    public List<SlipDetailOrderLine> Orders { get; } = [];
    public List<SlipDetailChargeLine> ChargeLines { get; } = [];

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

public class SlipDetailCustomer
{
    public long SlipCustomerId { get; set; }
    public int LineNo { get; set; }
    public string? CustomerLabel { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public string Status { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(CustomerLabel)
        ? $"客{LineNo}"
        : CustomerLabel;
}

public class SlipDetailNomination
{
    public long SlipCastId { get; set; }
    public long CastId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string NominationKind { get; set; } = string.Empty;
    public string NominationType { get; set; } = string.Empty;
    public string? NominationDisplayNameFromMaster { get; set; }
    public decimal NominationPrice { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string Status { get; set; } = string.Empty;

    public string CastDisplayName => DisplayName;

    public string NominationDisplayName => !string.IsNullOrWhiteSpace(NominationDisplayNameFromMaster)
        ? NominationDisplayNameFromMaster
        : !string.IsNullOrWhiteSpace(NominationKind)
            ? NominationKind
            : NominationType;
}

public class SlipDetailOrderLine
{
    public long OrderLineId { get; set; }
    public int LineNo { get; set; }
    public string ItemNameSnapshot { get; set; } = string.Empty;
    public string ItemType { get; set; } = "standard";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset OrderedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? BackCastId { get; set; }
    public string? BackCastDisplayName { get; set; }
    public string? BackCastDepartmentName { get; set; }
    public bool IsStandard => string.Equals(ItemType, "standard", StringComparison.Ordinal);
    public bool IsKaraoke => string.Equals(ItemType, "karaoke", StringComparison.Ordinal);
    public bool IsNominationFee => string.Equals(ItemType, "nomination_fee", StringComparison.Ordinal);
    public bool IsSystemItem => !IsStandard;
    public bool HasCastBack => BackCastId is > 0 && !string.IsNullOrWhiteSpace(BackCastDisplayName);
}

public class SlipDetailChargeLine
{
    public long ChargeLineId { get; set; }
    public int LineNo { get; set; }
    public string ChargeType { get; set; } = string.Empty;
    public string LineName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;

    public string ChargeTypeDisplayName => ChargeType switch
    {
        "karaoke" => "カラオケ",
        "adjustment" => "調整",
        _ => ChargeType
    };
}

public class SlipAdjustmentInputModel
{
    public bool HasContent => !string.IsNullOrWhiteSpace(LineName) || Amount != 0;

    [StringLength(160, ErrorMessage = "明細名は160文字以内で入力してください。")]
    public string? LineName { get; set; }

    [Range(-99999999, 99999999, ErrorMessage = "価格を確認してください。")]
    public decimal Amount { get; set; }
}

public class KaraokeQuantityInputModel
{
    public long SlipId { get; set; }

    [Range(0, 999, ErrorMessage = "カラオケ回数を確認してください。")]
    public decimal Quantity { get; set; }
}

public class OrderLineQuantityInputModel
{
    public long OrderLineId { get; set; }

    [Range(0, 999, ErrorMessage = "注文数量を確認してください。")]
    public decimal Quantity { get; set; }
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

public class BusinessDayAttendanceInput
{
    public long CastId { get; set; }
    public bool IsSelected { get; set; } = true;
    public string ClockInTime { get; set; } = string.Empty;
}
