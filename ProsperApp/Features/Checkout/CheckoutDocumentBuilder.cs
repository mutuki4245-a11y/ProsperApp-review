using System.Text.Json;
using ProsperApp.Models;

namespace ProsperApp.Services;

public static class CheckoutDocumentBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CheckoutTotals CalculateTotals(SlipDetail? detail)
    {
        var activeOrders = detail?.Orders
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal))
            .ToList() ?? [];
        var subtotal = activeOrders.Sum(x => x.Amount);
        var serviceTax = Math.Round(subtotal * 0.20m, 0, MidpointRounding.AwayFromZero);
        var adjustmentAmount = detail?.ChargeLines
            .Where(x => string.Equals(x.ChargeType, "adjustment", StringComparison.Ordinal) &&
                        string.Equals(x.Status, "active", StringComparison.Ordinal))
            .Sum(x => x.Amount) ?? 0;
        var total = subtotal + serviceTax + adjustmentAmount;

        return new CheckoutTotals
        {
            SubtotalAmount = subtotal,
            ServiceTaxAmount = serviceTax,
            AdjustmentAmount = adjustmentAmount,
            TotalAmount = Math.Max(total, 0)
        };
    }

    public static string BuildConfirmedSnapshotJson(SlipDetail detail, CheckoutTotals totals)
    {
        var activeOrders = detail.Orders
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal))
            .OrderBy(x => x.LineNo)
            .ThenBy(x => x.OrderLineId)
            .Select(x => new
            {
                order_line_id = x.OrderLineId,
                line_no = x.LineNo,
                item_name_snapshot = x.ItemNameSnapshot,
                item_type = x.ItemType,
                quantity = x.Quantity,
                unit_price = x.UnitPrice,
                amount = x.Amount,
                status = x.Status
            })
            .ToList();

        var activeCharges = detail.ChargeLines
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal) &&
                        string.Equals(x.ChargeType, "adjustment", StringComparison.Ordinal))
            .OrderBy(x => x.LineNo)
            .ThenBy(x => x.ChargeLineId)
            .Select(x => new
            {
                charge_line_id = x.ChargeLineId,
                line_no = x.LineNo,
                charge_type = x.ChargeType,
                line_name = x.LineName,
                quantity = x.Quantity,
                unit_price = x.UnitPrice,
                amount = x.Amount,
                status = x.Status
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            slip_id = detail.SlipId,
            business_date = detail.BusinessDate.ToString("yyyy-MM-dd"),
            table_id = detail.TableId,
            status = detail.Status,
            customer_count = detail.CustomerCount,
            subtotal_amount = totals.SubtotalAmount,
            service_tax_amount = totals.ServiceTaxAmount,
            adjustment_amount = totals.AdjustmentAmount,
            total_amount = totals.TotalAmount,
            orders = activeOrders,
            charges = activeCharges
        }, JsonOptions);
    }

    public static ReceiptPrintRequest BuildReceiptPrintRequest(
        SlipDetail detail,
        CheckoutTotals totals,
        CheckoutInputModel input,
        ConfirmCheckoutResult result,
        string storeName,
        IStoreClock storeClock)
    {
        if (result.CheckoutId is null)
        {
            throw new ArgumentException("CheckoutId is required for receipt printing.", nameof(result));
        }

        if (input.ClosedAt is null)
        {
            throw new ArgumentException("ClosedAt is required for receipt printing.", nameof(input));
        }

        var request = new ReceiptPrintRequest
        {
            CheckoutId = result.CheckoutId.Value,
            SlipId = detail.SlipId,
            SlipNo = detail.SlipNo,
            StoreName = storeName,
            TableDisplayName = detail.TableDisplayName,
            ClosedAt = storeClock.ToStoreDateTimeOffset(input.ClosedAt.Value),
            SubtotalAmount = totals.SubtotalAmount,
            ServiceTaxAmount = totals.ServiceTaxAmount,
            AdjustmentAmount = totals.AdjustmentAmount,
            TotalAmount = totals.TotalAmount,
            ReceivedAmount = input.ReceivedAmount,
            ChangeAmount = result.ChangeAmount
        };

        request.Lines.AddRange(detail.Orders
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal))
            .OrderBy(x => x.LineNo)
            .Select(x => new ReceiptPrintLine
            {
                LineType = "order",
                Name = x.ItemNameSnapshot,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice,
                Amount = x.Amount
            }));

        request.Lines.AddRange(detail.ChargeLines
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal))
            .OrderBy(x => x.LineNo)
            .Select(x => new ReceiptPrintLine
            {
                LineType = x.ChargeType,
                Name = x.LineName,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice,
                Amount = x.Amount
            }));

        request.Payments.AddRange(input.Payments
            .Where(x => x.IsSelected && x.Amount > 0)
            .Select(x => new ReceiptPrintPayment
            {
                MethodCode = x.MethodCode,
                MethodName = x.MethodName,
                Amount = x.Amount
            }));

        return request;
    }
}
