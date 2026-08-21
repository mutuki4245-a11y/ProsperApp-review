using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Orders;

public interface IOrderEntryApplicationService
{
    Task<OrderEntryPageState> LoadPageAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreOrderSlipOption>>> GetOpenSlipsAsync(CancellationToken ct);

    Task<Result<OrderEntryCandidates>> GetCandidatesAsync(CancellationToken ct);

    Task<Result<OrderEntrySubmitResult>> SubmitAsync(OrderEntrySubmitInput input, CancellationToken ct);
}

public sealed record OrderEntryPageState(
    StoreContext? StoreContext,
    StoreBusinessDay? BusinessDay,
    IReadOnlyList<StoreOrderItemOption> Items,
    IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts,
    IReadOnlyList<PageLoadIssue> LoadIssues,
    DateTimeOffset? LastUpdatedAt);
