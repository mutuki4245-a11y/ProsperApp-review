using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Pricing;

public interface IStorePricingPlanRepository
{
    Task<Result<StorePricingPlanInputModel>> GetAsync(CancellationToken ct);

    Task<StorePricingPlanSaveResult> SaveAsync(StorePricingPlanInputModel plan, CancellationToken ct);
}
