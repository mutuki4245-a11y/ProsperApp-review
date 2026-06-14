using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IStoreOrderRepository
{
    Task<IReadOnlyList<StoreOrderSlipOption>> GetOpenSlipsAsync(long businessDayId, CancellationToken ct);

    Task<IReadOnlyList<StoreOrderItemOption>> GetItemsAsync(CancellationToken ct);

    Task<IReadOnlyList<StoreOrderAttendanceCastOption>> GetAttendanceCastsAsync(long businessDayId, CancellationToken ct);

    Task<AddStoreOrderLinesResult> AddOrderLinesAsync(long slipId, IReadOnlyList<OrderQueueInputModel> lines, CancellationToken ct);
}
