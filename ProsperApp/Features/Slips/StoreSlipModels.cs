using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ProsperApp.Services;

namespace ProsperApp.Features.Slips;

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

    [Display(Name = "お客様数")]
    public int CustomerCount => CustomerLabels.Count;

    [Display(Name = "お客様情報")]
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

/// <summary>
/// 営業中トップから送る、1操作だけの非同期編集要求です。
/// 入力の確定と全体スナップショットの作成は同一RPCで行います。
/// </summary>
public class BusinessSlipEditorOperationInput
{
    public string OperationId { get; set; } = string.Empty;

    public string? ClientDraftId { get; set; }

    public long? SlipId { get; set; }

    public string OperationType { get; set; } = string.Empty;

    public JsonElement Payload { get; set; }
}

/// <summary>
/// 営業中トップの未送信行を一度に確定する要求です。
/// BatchId は応答未達時にも同じ値で再送し、追加行の二重登録を防ぎます。
/// </summary>
public class BusinessHomeChangeFlushInput
{
    public string BatchId { get; set; } = string.Empty;

    public long? ExpectedBusinessDayId { get; set; }

    public long? ExpectedBusinessDayRevision { get; set; }

    public DateOnly? BusinessDate { get; set; }

    public List<BusinessSlipEditorOperationInput> Operations { get; set; } = [];

    public List<BusinessHomeKaraokeLineInput> KaraokeLines { get; set; } = [];
}

public class BusinessHomeKaraokeLineInput
{
    public string DraftId { get; set; } = string.Empty;

    public long SlipId { get; set; }

    public decimal Quantity { get; set; }
}

public class BusinessHomeChangeFlushResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public string Status { get; init; } = "unavailable";

    public JsonElement BusinessDay { get; init; }

    public long BusinessDayRevision { get; init; }

    public JsonElement Snapshot { get; init; }

    public JsonElement OperationResults { get; init; }

    public JsonElement KaraokeResults { get; init; }

    public static BusinessHomeChangeFlushResult Success(
        string status,
        JsonElement businessDay,
        long businessDayRevision,
        JsonElement snapshot,
        JsonElement operationResults,
        JsonElement karaokeResults) => new()
    {
        Succeeded = true,
        Status = status,
        BusinessDay = businessDay,
        BusinessDayRevision = businessDayRevision,
        Snapshot = snapshot,
        OperationResults = operationResults,
        KaraokeResults = karaokeResults
    };

    public static BusinessHomeChangeFlushResult Failed(string message) => new()
    {
        Succeeded = false,
        ErrorMessage = message
    };
}

public class BusinessHomeBootstrapResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public StoreContext? StoreContext { get; init; }

    public StoreBusinessDay? BusinessDay { get; init; }

    public IReadOnlyList<StoreTableOption> Tables { get; init; } = [];

    public IReadOnlyList<NominationBackMasterItem> NominationOptions { get; init; } = [];

    public IReadOnlyList<StoreOrderItemOption> OrderItems { get; init; } = [];

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; init; } = [];

    public IReadOnlyList<CheckoutPaymentMethod> PaymentMethods { get; init; } = [];

    public JsonElement? Snapshot { get; init; }

    public static BusinessHomeBootstrapResult Success(
        StoreContext storeContext,
        StoreBusinessDay? businessDay,
        IReadOnlyList<StoreTableOption> tables,
        IReadOnlyList<NominationBackMasterItem> nominationOptions,
        IReadOnlyList<StoreOrderItemOption> orderItems,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts,
        IReadOnlyList<CheckoutPaymentMethod> paymentMethods,
        JsonElement? snapshot) => new()
        {
            Succeeded = true,
            StoreContext = storeContext,
            BusinessDay = businessDay,
            Tables = tables,
            NominationOptions = nominationOptions,
            OrderItems = orderItems,
            AttendanceCasts = attendanceCasts,
            PaymentMethods = paymentMethods,
            Snapshot = snapshot?.Clone()
        };

    public static BusinessHomeBootstrapResult Failed(string message) => new()
    {
        Succeeded = false,
        ErrorMessage = message
    };
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
    public long BusinessUiRevision { get; set; }
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

public class BusinessDayStaffAttendanceInput
{
    public long StaffId { get; set; }
    public bool IsSelected { get; set; } = true;
    public string ClockInTime { get; set; } = string.Empty;
}
