using System.Text.Json;
using ProsperApp.Models;

namespace ProsperApp.Services;

public static class CheckoutDocumentBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CheckoutDocument Build(SlipDetail? detail)
    {
        var totals = CalculateTotals(detail);
        var confirmedSnapshotJson = detail is null
            ? "{}"
            : BuildConfirmedSnapshotJson(detail, totals);

        return new CheckoutDocument(detail, totals, confirmedSnapshotJson);
    }

    private static CheckoutTotals CalculateTotals(SlipDetail? detail)
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

    private static string BuildConfirmedSnapshotJson(SlipDetail detail, CheckoutTotals totals)
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
}

public sealed class CheckoutDocument
{
    private readonly SlipDetail? _detail;

    internal CheckoutDocument(
        SlipDetail? detail,
        CheckoutTotals totals,
        string confirmedSnapshotJson)
    {
        _detail = detail;
        Totals = totals;
        ConfirmedSnapshotJson = confirmedSnapshotJson;
    }

    public CheckoutTotals Totals { get; }

    public string ConfirmedSnapshotJson { get; }

    public ReceiptPrintRequest BuildReceiptPrintRequest(
        CheckoutInputModel input,
        ConfirmCheckoutResult result,
        string storeName,
        DateTimeOffset closedAt)
    {
        if (_detail is null)
        {
            throw new InvalidOperationException("Slip detail is required for receipt printing.");
        }

        if (result.CheckoutId is null)
        {
            throw new ArgumentException("CheckoutId is required for receipt printing.", nameof(result));
        }

        var request = new ReceiptPrintRequest
        {
            CheckoutId = result.CheckoutId.Value,
            SlipId = _detail.SlipId,
            SlipNo = _detail.SlipNo,
            StoreName = storeName,
            TableDisplayName = _detail.TableDisplayName,
            ClosedAt = closedAt,
            SubtotalAmount = Totals.SubtotalAmount,
            ServiceTaxAmount = Totals.ServiceTaxAmount,
            AdjustmentAmount = Totals.AdjustmentAmount,
            TotalAmount = Totals.TotalAmount,
            ReceivedAmount = input.ReceivedAmount,
            ChangeAmount = result.ChangeAmount
        };

        request.Lines.AddRange(_detail.Orders
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

        request.Lines.AddRange(_detail.ChargeLines
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
