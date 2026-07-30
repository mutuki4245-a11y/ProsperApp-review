using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using ProsperApp.Models;

namespace ProsperApp.Features.BusinessHome;

public static class BusinessHomeOperationTypes
{
    public const string AddCustomer = "add_customer";
    public const string UpdateCustomer = "update_customer";
    public const string LeaveCustomer = "leave_customer";
    public const string AddNomination = "add_nomination";
    public const string CancelNomination = "cancel_nomination";
    public const string AddAdjustment = "add_adjustment";
    public const string VoidAdjustment = "void_adjustment";
    public const string AddOrder = "add_order";
    public const string VoidOrder = "void_order";

    public static readonly IReadOnlyList<string> All =
    [
        AddCustomer,
        UpdateCustomer,
        LeaveCustomer,
        AddNomination,
        CancelNomination,
        AddAdjustment,
        VoidAdjustment,
        AddOrder,
        VoidOrder
    ];
}

public static class BusinessHomeOperationParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Result<IBusinessHomeOperationPayload> Parse(BusinessSlipEditorOperationInput operation)
    {
        if (operation.SlipId <= 0 ||
            string.IsNullOrWhiteSpace(operation.OperationId) ||
            operation.OperationId.Length > 100)
        {
            return Invalid();
        }

        try
        {
            IBusinessHomeOperationPayload? payload = operation.OperationType switch
            {
                BusinessHomeOperationTypes.AddCustomer => Read<AddCustomerPayload>(operation.Payload),
                BusinessHomeOperationTypes.UpdateCustomer => Read<UpdateCustomerPayload>(operation.Payload),
                BusinessHomeOperationTypes.LeaveCustomer => Read<LeaveCustomerPayload>(operation.Payload),
                BusinessHomeOperationTypes.AddNomination => Read<AddNominationPayload>(operation.Payload),
                BusinessHomeOperationTypes.CancelNomination => Read<CancelNominationPayload>(operation.Payload),
                BusinessHomeOperationTypes.AddAdjustment => Read<AddAdjustmentPayload>(operation.Payload),
                BusinessHomeOperationTypes.VoidAdjustment => Read<VoidAdjustmentPayload>(operation.Payload),
                BusinessHomeOperationTypes.AddOrder => Read<AddOrderPayload>(operation.Payload),
                BusinessHomeOperationTypes.VoidOrder => Read<VoidOrderPayload>(operation.Payload),
                _ => null
            };

            return payload is not null && IsValid(payload)
                ? Result<IBusinessHomeOperationPayload>.Success(payload)
                : Invalid();
        }
        catch (JsonException)
        {
            return Invalid();
        }
    }

    private static T? Read<T>(JsonElement payload)
        where T : class, IBusinessHomeOperationPayload
    {
        return payload.ValueKind == JsonValueKind.Object
            ? payload.Deserialize<T>(JsonOptions)
            : null;
    }

    private static bool IsValid(IBusinessHomeOperationPayload payload)
    {
        return payload switch
        {
            AddCustomerPayload value =>
                IsClockTime(value.EnteredTime) &&
                (value.CustomerLabel?.Length ?? 0) <= 100,
            UpdateCustomerPayload value =>
                value.SlipCustomerId > 0 &&
                (value.CustomerLabel?.Length ?? 0) <= 100,
            LeaveCustomerPayload value =>
                value.SlipCustomerId > 0 && IsClockTime(value.LeftTime),
            AddNominationPayload value =>
                value.CastId > 0 &&
                !string.IsNullOrWhiteSpace(value.NominationKind) &&
                value.NominationPrice is >= 1000 and <= 20000 &&
                value.NominationPrice % 1000 == 0,
            CancelNominationPayload value => value.SlipCastId > 0,
            AddAdjustmentPayload value =>
                !string.IsNullOrWhiteSpace(value.LineName) &&
                value.LineName.Length <= 160 &&
                decimal.Truncate(value.Amount) == value.Amount &&
                Math.Abs(value.Amount) <= 99999999,
            VoidAdjustmentPayload value => value.ChargeLineId > 0,
            AddOrderPayload value =>
                value.ItemId > 0 &&
                value.Quantity is >= 1 and <= 999 &&
                (value.CastBackCastId is null or > 0),
            VoidOrderPayload value => value.OrderLineId > 0,
            _ => false
        };
    }

    private static bool IsClockTime(string? value)
    {
        return TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static Result<IBusinessHomeOperationPayload> Invalid() =>
        Result<IBusinessHomeOperationPayload>.Failure(
            ResultFailureKind.InvalidInput,
            "保存内容を確認してください。");
}

public interface IBusinessHomeOperationPayload;

public sealed record AddCustomerPayload(
    [property: JsonPropertyName("customer_label")] string? CustomerLabel,
    [property: JsonPropertyName("entered_time")] string? EnteredTime) : IBusinessHomeOperationPayload;

public sealed record UpdateCustomerPayload(
    [property: JsonPropertyName("slip_customer_id")] long SlipCustomerId,
    [property: JsonPropertyName("customer_label")] string? CustomerLabel) : IBusinessHomeOperationPayload;

public sealed record LeaveCustomerPayload(
    [property: JsonPropertyName("slip_customer_id")] long SlipCustomerId,
    [property: JsonPropertyName("left_time")] string? LeftTime) : IBusinessHomeOperationPayload;

public sealed record AddNominationPayload(
    [property: JsonPropertyName("cast_id")] long CastId,
    [property: JsonPropertyName("nomination_kind")] string? NominationKind,
    [property: JsonPropertyName("nomination_price")] decimal NominationPrice,
    [property: JsonPropertyName("allow_duplicate")] bool AllowDuplicate) : IBusinessHomeOperationPayload;

public sealed record CancelNominationPayload(
    [property: JsonPropertyName("slip_cast_id")] long SlipCastId) : IBusinessHomeOperationPayload;

public sealed record AddAdjustmentPayload(
    [property: JsonPropertyName("line_name")] string? LineName,
    [property: JsonPropertyName("amount")] decimal Amount) : IBusinessHomeOperationPayload;

public sealed record VoidAdjustmentPayload(
    [property: JsonPropertyName("charge_line_id")] long ChargeLineId) : IBusinessHomeOperationPayload;

public sealed record AddOrderPayload(
    [property: JsonPropertyName("item_id")] long ItemId,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("cast_back_cast_id")] long? CastBackCastId) : IBusinessHomeOperationPayload;

public sealed record VoidOrderPayload(
    [property: JsonPropertyName("order_line_id")] long OrderLineId) : IBusinessHomeOperationPayload;
