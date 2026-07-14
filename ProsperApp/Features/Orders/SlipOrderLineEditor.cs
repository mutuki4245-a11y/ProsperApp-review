using ProsperApp.Models;

namespace ProsperApp.Services;

public static class SlipOrderLineEditor
{
    private const string OrderQuantityLinesKey = "OrderQuantityLines";

    public static bool CanVoidStandardOrderLine(SlipDetail detail, long orderLineId)
    {
        return detail.Orders.Any(x => x.OrderLineId == orderLineId &&
                                      string.Equals(x.Status, "active", StringComparison.Ordinal) &&
                                      x.IsStandard);
    }

    public static OrderLineQuantityEdit PrepareQuantitySave(
        SlipDetail detail,
        IEnumerable<OrderLineQuantityInputModel> inputLines)
    {
        var lines = NormalizeQuantities(inputLines);
        var errors = ValidateQuantities(detail, lines);
        return new OrderLineQuantityEdit(lines, errors);
    }

    private static List<OrderLineQuantityInputModel> NormalizeQuantities(IEnumerable<OrderLineQuantityInputModel> inputLines)
    {
        return inputLines
            .Where(x => x.OrderLineId > 0)
            .GroupBy(x => x.OrderLineId)
            .Select(x => new OrderLineQuantityInputModel
            {
                OrderLineId = x.Key,
                Quantity = x.Last().Quantity
            })
            .ToList();
    }

    private static IReadOnlyList<OrderLineQuantityValidationError> ValidateQuantities(
        SlipDetail detail,
        IReadOnlyList<OrderLineQuantityInputModel> lines)
    {
        List<OrderLineQuantityValidationError> errors = [];

        if (!string.Equals(detail.Status, "open", StringComparison.Ordinal))
        {
            errors.Add(new OrderLineQuantityValidationError(string.Empty, "営業中の伝票のみ注文数量を訂正できます。"));
        }

        if (lines.Count == 0)
        {
            errors.Add(new OrderLineQuantityValidationError(OrderQuantityLinesKey, "訂正する注文数量がありません。"));
            return errors;
        }

        var editableOrderIds = detail.Orders
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal) && x.IsStandard)
            .Select(x => x.OrderLineId)
            .ToHashSet();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!editableOrderIds.Contains(line.OrderLineId))
            {
                errors.Add(new OrderLineQuantityValidationError(
                    $"{OrderQuantityLinesKey}[{i}].{nameof(OrderLineQuantityInputModel.OrderLineId)}",
                    "訂正する注文を確認してください。"));
            }

            if (line.Quantity < 0 || line.Quantity > 999 || line.Quantity != decimal.Truncate(line.Quantity))
            {
                errors.Add(new OrderLineQuantityValidationError(
                    $"{OrderQuantityLinesKey}[{i}].{nameof(OrderLineQuantityInputModel.Quantity)}",
                    "注文数量を確認してください。"));
            }
        }

        return errors;
    }
}

public sealed record OrderLineQuantityEdit(
    List<OrderLineQuantityInputModel> Lines,
    IReadOnlyList<OrderLineQuantityValidationError> Errors);

public sealed record OrderLineQuantityValidationError(string Key, string Message);
