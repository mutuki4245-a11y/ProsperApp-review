namespace ProsperApp.Models;

public class StoreOrderSlipOption
{
    public long SlipId { get; set; }
    public long? TableId { get; set; }
    public string? TableCode { get; set; }
    public string? TableName { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
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

public class StoreOrderItemOption
{
    public long ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string ItemType { get; set; } = "standard";
    public decimal DefaultPrice { get; set; }
    public string? CategoryCode { get; set; }
    public string CategoryName { get; set; } = "未分類";
    public bool IsCastBackTarget { get; set; }
    public decimal CastBackRegularUnitAmount { get; set; }
    public decimal CastBackNominationUnitAmount { get; set; }
    public string CastBackType { get; set; } = "drink";
    public bool IsStandard => string.Equals(ItemType, "standard", StringComparison.Ordinal);
    public bool IsKaraoke => string.Equals(ItemType, "karaoke", StringComparison.Ordinal);
    public bool IsNominationFee => string.Equals(ItemType, "nomination_fee", StringComparison.Ordinal);
    public bool IsSystemItem => !IsStandard;
}

public class StoreOrderAttendanceCastOption
{
    public long CastId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? ClockInTime { get; set; }

    public string SearchDisplayName => DisplayName;
}

public class OrderQueueInputModel
{
    public long? SlipId { get; set; }

    public long ItemId { get; set; }

    public int Quantity { get; set; }

    public long? CastBackCastId { get; set; }
}

public class AddStoreOrderLinesResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public int InsertedCount { get; init; }

    public static AddStoreOrderLinesResult Success(int insertedCount)
    {
        return new AddStoreOrderLinesResult { Succeeded = true, InsertedCount = insertedCount };
    }

    public static AddStoreOrderLinesResult Failed(string message)
    {
        return new AddStoreOrderLinesResult { Succeeded = false, ErrorMessage = message };
    }
}
