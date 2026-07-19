using ProsperApp.Models;

namespace ProsperApp.Services;

public interface ICheckoutRepository
{
    Task<CheckoutStatementResult> IssueCheckoutStatementAsync(long slipId, DateTimeOffset closedAt, CancellationToken ct);

    Task<CheckoutStatementResult> GetCheckoutStatementPrintDataAsync(long slipId, CancellationToken ct);

    Task<ReleaseCheckoutReadyResult> ReleaseCheckoutReadyAsync(long slipId, CancellationToken ct);

    Task<ConfirmCheckoutResult> ConfirmCheckoutAsync(
        long slipId,
        IReadOnlyList<CheckoutPaymentInputModel> payments,
        decimal? receivedAmount,
        CancellationToken ct);

    Task<ReceiptPrintDataResult> GetCheckoutReceiptPrintDataAsync(long slipId, CancellationToken ct);

    Task<CancelCheckoutResult> CancelCheckoutAsync(long slipId, CancellationToken ct);
}
