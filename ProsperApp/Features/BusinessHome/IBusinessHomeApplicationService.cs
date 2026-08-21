using System.Text.Json;
using ProsperApp.Features.Shared;

namespace ProsperApp.Features.BusinessHome;

public interface IBusinessHomeApplicationService
{
    Task<Result<BusinessHomeSnapshotState>> GetSnapshotAsync(long? knownRevision, CancellationToken ct);

    Task<BusinessHomePageState> LoadPageAsync(
        bool ordersEnabled,
        bool checkoutEnabled,
        bool includeAttendanceCasts,
        CancellationToken ct);

    Task<Result<BusinessHomeFlushOutput>> FlushAsync(
        BusinessHomeChangeFlushInput input,
        CancellationToken ct);

    Task<Result<CheckoutMutationResult>> IssueCheckoutStatementV2Async(
        IssueCheckoutStatementV2Mutation mutation,
        CancellationToken ct);

    Task<CheckoutStatementResult> GetCheckoutStatementPrintDataAsync(
        long slipId,
        CancellationToken ct);

    Task<Result<CheckoutMutationResult>> ReleaseCheckoutReadyV2Async(
        ReleaseCheckoutReadyV2Mutation mutation,
        CancellationToken ct);

    Task<Result<CheckoutMutationResult>> ConfirmCheckoutV2Async(
        ConfirmCheckoutV2Mutation mutation,
        CancellationToken ct);

    Task<ReceiptPrintDataResult> GetCheckoutReceiptPrintDataAsync(
        long slipId,
        CancellationToken ct);

    Task<Result<CheckoutMutationResult>> CancelCheckoutV2Async(
        CancelCheckoutV2Mutation mutation,
        CancellationToken ct);
}

public sealed record BusinessHomeSnapshotState(
    StoreBusinessDay? BusinessDay,
    DateOnly BusinessDate,
    long Revision,
    bool Unchanged,
    System.Text.Json.JsonElement? AttendanceCasts,
    JsonElement? Snapshot);

public sealed record BusinessHomeFlushOutput(
    string BatchId,
    string Status,
    JsonElement BusinessDay,
    long BusinessDayRevision,
    JsonElement Snapshot,
    JsonElement OperationResults,
    JsonElement KaraokeResults);

public sealed record BusinessHomePageState(
    StoreContext? StoreContext,
    StoreBusinessDay? BusinessDay,
    DateOnly BusinessDate,
    IReadOnlyList<StoreTableOption> Tables,
    IReadOnlyList<NominationBackMasterItem> NominationOptions,
    IReadOnlyList<StoreOrderItemOption> OrderItems,
    IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts,
    IReadOnlyList<CheckoutPaymentMethod> PaymentMethods,
    JsonElement? InitialSnapshot,
    IReadOnlyList<PageLoadIssue> LoadIssues,
    DateTimeOffset? LastUpdatedAt);
