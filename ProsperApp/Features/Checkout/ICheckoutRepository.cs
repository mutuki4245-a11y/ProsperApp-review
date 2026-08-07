using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Checkout;

public interface ICheckoutRepository
{
    Task<Result<IReadOnlyList<CheckoutPaymentMethod>>> GetPaymentMethodsAsync(CancellationToken ct);

    Task<Result<CheckoutMutationResult>> IssueCheckoutStatementV2Async(
        IssueCheckoutStatementV2Mutation mutation,
        CancellationToken ct);

    Task<Result<CheckoutMutationResult>> ReleaseCheckoutReadyV2Async(
        ReleaseCheckoutReadyV2Mutation mutation,
        CancellationToken ct);

    Task<Result<CheckoutMutationResult>> ConfirmCheckoutV2Async(
        ConfirmCheckoutV2Mutation mutation,
        CancellationToken ct);

    Task<Result<CheckoutMutationResult>> CancelCheckoutV2Async(
        CancelCheckoutV2Mutation mutation,
        CancellationToken ct);

    Task<CheckoutStatementResult> GetCheckoutStatementPrintDataAsync(long slipId, CancellationToken ct);

    Task<ReceiptPrintDataResult> GetCheckoutReceiptPrintDataAsync(long slipId, CancellationToken ct);

}
