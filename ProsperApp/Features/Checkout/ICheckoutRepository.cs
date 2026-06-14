using ProsperApp.Models;

namespace ProsperApp.Services;

public interface ICheckoutRepository
{
    Task<ConfirmCheckoutResult> ConfirmCheckoutAsync(long slipId, CheckoutInputModel input, CancellationToken ct);
}
