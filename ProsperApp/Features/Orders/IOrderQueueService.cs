using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IOrderQueueService
{
    List<OrderQueueInputModel> ReadPostedQueue(string? orderQueueJson, IEnumerable<OrderQueueInputModel> fallbackLines);

    IReadOnlyList<string> Validate(
        IReadOnlyList<OrderQueueInputModel> queueLines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts,
        bool requireAttendingCastForBackTarget,
        string missingItemsMessage = "商品マスタが未登録です。");
}
