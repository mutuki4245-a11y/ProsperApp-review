using System.Text.Json;

namespace ProsperApp.Features.Orders;

public sealed class OrderQueueService : IOrderQueueService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public List<OrderQueueInputModel> ReadPostedQueue(string? orderQueueJson, IEnumerable<OrderQueueInputModel> fallbackLines)
    {
        var postedLines = ParsePostedQueue(orderQueueJson);
        return Normalize(postedLines.Count > 0 ? postedLines : fallbackLines);
    }

    private static List<OrderQueueInputModel> Normalize(IEnumerable<OrderQueueInputModel> queueLines)
    {
        return queueLines
            .Where(x => x.ItemId > 0 && x.Quantity > 0)
            .GroupBy(x => new { x.SlipId, x.ItemId, x.CastBackCastId })
            .Select(group => new OrderQueueInputModel
            {
                SlipId = group.Key.SlipId,
                ItemId = group.Key.ItemId,
                Quantity = group.Sum(x => x.Quantity),
                CastBackCastId = group.Key.CastBackCastId
            })
            .ToList();
    }

    public IReadOnlyList<string> ValidateOrderEntryQueue(
        IReadOnlyList<OrderQueueInputModel> queueLines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts)
    {
        return Validate(
            queueLines,
            items,
            attendanceCasts,
            requireAttendingCastForBackTarget: false,
            missingItemsMessage: "注文可能な商品が未登録です。商品マスタを確認してください。");
    }

    public IReadOnlyList<string> ValidateSlipOrderQueue(
        IReadOnlyList<OrderQueueInputModel> queueLines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts)
    {
        return Validate(
            queueLines,
            items,
            attendanceCasts,
            requireAttendingCastForBackTarget: true,
            missingItemsMessage: "注文可能な商品が未登録です。");
    }

    private static IReadOnlyList<string> Validate(
        IReadOnlyList<OrderQueueInputModel> queueLines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts,
        bool requireAttendingCastForBackTarget,
        string missingItemsMessage)
    {
        List<string> errors = [];

        if (items.Count == 0)
        {
            errors.Add(missingItemsMessage);
        }

        if (queueLines.Count == 0)
        {
            errors.Add("注文キューに商品を追加してください。");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var allowedItemIds = items.Select(x => x.ItemId).ToHashSet();
        var attendingCastIds = attendanceCasts.Select(x => x.CastId).ToHashSet();
        foreach (var line in queueLines)
        {
            var item = items.FirstOrDefault(x => x.ItemId == line.ItemId);
            if (item is null || !allowedItemIds.Contains(line.ItemId))
            {
                errors.Add("注文キューに利用できない商品があります。");
                break;
            }

            if (!item.IsCastBackTarget)
            {
                continue;
            }

            if (line.CastBackCastId is not null &&
                requireAttendingCastForBackTarget &&
                !attendingCastIds.Contains(line.CastBackCastId.Value))
            {
                errors.Add("バック対象商品のキャストは出勤キャストから選択してください。");
                break;
            }
        }

        return errors;
    }

    private static List<OrderQueueInputModel> ParsePostedQueue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            var lines = JsonSerializer.Deserialize<List<PostedOrderQueueLine>>(value, Options);
            return lines?
                .Where(x => x.ItemId > 0 && x.Quantity > 0)
                .Select(x => new OrderQueueInputModel
                {
                    SlipId = x.SlipId,
                    ItemId = x.ItemId,
                    Quantity = x.Quantity,
                    CastBackCastId = x.CastBackCastId
                })
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class PostedOrderQueueLine
    {
        public long? SlipId { get; set; }

        public long ItemId { get; set; }

        public int Quantity { get; set; }

        public long? CastBackCastId { get; set; }
    }
}
