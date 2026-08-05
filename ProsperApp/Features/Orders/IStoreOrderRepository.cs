using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Orders;

public interface IStoreOrderRepository
{
    Task<Result<IReadOnlyList<StoreOrderSlipOption>>> GetOpenSlipsAsync(long businessDayId, CancellationToken ct);

    Task<Result<OrderEntryCandidates>> GetCurrentCandidatesAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreOrderItemOption>>> GetItemsAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreOrderAttendanceCastOption>>> GetAttendanceCastsAsync(long businessDayId, CancellationToken ct);

    Task<AddStoreOrderLinesResult> AddOrderLinesAsync(long slipId, IReadOnlyList<OrderQueueInputModel> lines, CancellationToken ct);
}

public sealed record OrderEntryCandidates(
    StoreBusinessDay? BusinessDay,
    string Revision,
    IReadOnlyList<StoreOrderSlipOption> Slips,
    IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts);
