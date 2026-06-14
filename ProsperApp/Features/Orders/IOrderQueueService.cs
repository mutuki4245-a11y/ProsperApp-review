using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IOrderQueueService
{
    List<OrderQueueInputModel> Normalize(IEnumerable<OrderQueueInputModel> queueLines);

    IReadOnlyList<string> Validate(
        IReadOnlyList<OrderQueueInputModel> queueLines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts,
        bool requireAttendingCastForBackTarget,
        string missingItemsMessage = "商品マスタが未登録です。");
}
