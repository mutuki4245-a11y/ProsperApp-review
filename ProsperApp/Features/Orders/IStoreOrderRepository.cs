using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Orders;

public interface IStoreOrderRepository
{
    Task<Result<OrderEntryCandidates>> GetCurrentCandidatesAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreOrderItemOption>>> GetItemsAsync(CancellationToken ct);

    Task<Result<OrderEntrySubmitResult>> SubmitCurrentAsync(OrderEntrySubmitInput input, CancellationToken ct);
}

public sealed record OrderEntryCandidates(
    StoreBusinessDay? BusinessDay,
    string Revision,
    IReadOnlyList<StoreOrderSlipOption> Slips,
    IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts);
