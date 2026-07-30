using System.Text.Json;
using ProsperApp.Features.Shared;

namespace ProsperApp.Features.BusinessHome;

public interface IBusinessHomeApplicationService
{
    Task<Result<BusinessHomeSnapshotState>> GetSnapshotAsync(CancellationToken ct);

    Task<BusinessHomePageState> LoadPageAsync(
        bool ordersEnabled,
        bool checkoutEnabled,
        bool includeAttendanceCasts,
        CancellationToken ct);

    Task<Result<IReadOnlyList<StoreOrderAttendanceCastOption>>> GetAttendanceCastsAsync(
        CancellationToken ct);

    Task<Result<BusinessHomeFlushOutput>> FlushAsync(
        BusinessHomeChangeFlushInput input,
        CancellationToken ct);

    Task<CreateSlipResult> CreateSlipAsync(
        CreateSlipInputModel input,
        CancellationToken ct);

    Task<CheckoutStatementResult> IssueCheckoutStatementAsync(
        long slipId,
        DateTimeOffset closedAt,
        CancellationToken ct);

    Task<CheckoutStatementResult> GetCheckoutStatementPrintDataAsync(
        long slipId,
        CancellationToken ct);

    Task<ReleaseCheckoutReadyResult> ReleaseCheckoutReadyAsync(
        long slipId,
        CancellationToken ct);

    Task<ConfirmCheckoutResult> ConfirmCheckoutAsync(
        long slipId,
        IReadOnlyList<CheckoutPaymentInputModel> payments,
        decimal? receivedAmount,
        CancellationToken ct);

    Task<ReceiptPrintDataResult> GetCheckoutReceiptPrintDataAsync(
        long slipId,
        CancellationToken ct);

    Task<CancelCheckoutResult> CancelCheckoutAsync(
        long slipId,
        CancellationToken ct);
}

public sealed record BusinessHomeSnapshotState(
    StoreBusinessDay? BusinessDay,
    DateOnly BusinessDate,
    JsonElement? Snapshot);

public sealed record BusinessHomeFlushOutput(
    string BatchId,
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
    IReadOnlyList<PageLoadIssue> LoadIssues,
    DateTimeOffset? LastUpdatedAt);
