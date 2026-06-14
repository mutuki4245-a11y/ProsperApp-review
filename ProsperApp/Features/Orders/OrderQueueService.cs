using ProsperApp.Models;

namespace ProsperApp.Services;

public sealed class OrderQueueService : IOrderQueueService
{
    public List<OrderQueueInputModel> Normalize(IEnumerable<OrderQueueInputModel> queueLines)
    {
        return queueLines
            .Where(x => x.ItemId > 0 && x.Quantity > 0)
            .GroupBy(x => new { x.ItemId, x.CastBackCastId })
            .Select(group => new OrderQueueInputModel
            {
                ItemId = group.Key.ItemId,
                Quantity = group.Sum(x => x.Quantity),
                CastBackCastId = group.Key.CastBackCastId
            })
            .ToList();
    }

    public IReadOnlyList<string> Validate(
        IReadOnlyList<OrderQueueInputModel> queueLines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts,
        bool requireAttendingCastForBackTarget,
        string missingItemsMessage = "商品マスタが未登録です。")
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

            if (line.CastBackCastId is null)
            {
                errors.Add("バック対象商品のキャストを選択してください。");
                break;
            }

            if (requireAttendingCastForBackTarget && !attendingCastIds.Contains(line.CastBackCastId.Value))
            {
                errors.Add("バック対象商品のキャストは出勤キャストから選択してください。");
                break;
            }
        }

        return errors;
    }
}
