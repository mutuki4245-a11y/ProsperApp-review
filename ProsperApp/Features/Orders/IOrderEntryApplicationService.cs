using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Orders;

public interface IOrderEntryApplicationService
{
    Task<OrderEntryPageState> LoadPageAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreOrderSlipOption>>> GetOpenSlipsAsync(CancellationToken ct);

    IReadOnlyList<OrderQueueInputModel> ReadPostedQueue(
        string? queueJson,
        IReadOnlyList<OrderQueueInputModel> fallbackLines);

    IReadOnlyList<string> ValidateQueue(
        IReadOnlyList<OrderQueueInputModel> lines,
        IReadOnlyList<StoreOrderItemOption> items,
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts);

    Task<AddStoreOrderLinesResult> AddOrderLinesAsync(
        IReadOnlyList<OrderQueueInputModel> lines,
        CancellationToken ct);
}

public sealed record OrderEntryPageState(
    StoreContext? StoreContext,
    StoreBusinessDay? BusinessDay,
    IReadOnlyList<StoreOrderItemOption> Items,
    IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts,
    IReadOnlyList<PageLoadIssue> LoadIssues,
    DateTimeOffset? LastUpdatedAt);
