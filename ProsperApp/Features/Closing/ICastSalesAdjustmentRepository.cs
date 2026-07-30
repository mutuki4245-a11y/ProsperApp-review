using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Closing;

public interface ICastSalesAdjustmentRepository
{
    Task<Result<CastSalesAdjustmentOverview>> GetOverviewAsync(long businessDayId, CancellationToken ct);

    Task<CastSalesAdjustmentSaveResult> SaveAsync(CastSalesAdjustmentSaveInput input, CancellationToken ct);

    Task<CastSalesAdjustmentSaveResult> SaveBatchAsync(
        long businessDayId,
        IReadOnlyCollection<CastSalesAdjustmentSaveInput> inputs,
        CancellationToken ct);
}
