using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Closing;

public interface ICastSalesAdjustmentRepository
{
    Task<Result<CastSalesAdjustmentOverview>> GetCurrentOverviewAsync(CancellationToken ct);

    Task<Result<CastSalesAdjustmentMutationResult>> SaveCurrentAsync(
        SaveCurrentCastSalesAdjustmentMutation mutation,
        CancellationToken ct);

    Task<Result<CastSalesAdjustmentMutationResult>> ConfirmCurrentAsync(
        ConfirmCurrentCastSalesAdjustmentsMutation mutation,
        CancellationToken ct);
}
