using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IStorePricingPlanRepository
{
    Task<StorePricingPlanInputModel> GetAsync(CancellationToken ct);

    Task<StorePricingPlanSaveResult> SaveAsync(StorePricingPlanInputModel plan, CancellationToken ct);
}
