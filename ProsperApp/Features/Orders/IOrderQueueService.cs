using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IOrderQueueService
{
    List<OrderQueueInputModel> ReadPostedQueue(string? orderQueueJson, IEnumerable<OrderQueueInputModel> fallbackLines);

    IReadOnlyList<string> ValidateOrderEntryQueue(
        IReadOnlyList<OrderQueueInputModel> queueLines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts);

    IReadOnlyList<string> ValidateSlipOrderQueue(
        IReadOnlyList<OrderQueueInputModel> queueLines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts);
}
