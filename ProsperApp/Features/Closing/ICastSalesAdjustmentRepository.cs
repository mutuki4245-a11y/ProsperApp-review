using ProsperApp.Models;
using ProsperApp.Features.Shared;

namespace ProsperApp.Services;

public interface ICastSalesAdjustmentRepository
{
    Task<Result<CastSalesAdjustmentOverview>> GetOverviewAsync(long businessDayId, CancellationToken ct);

    Task<CastSalesAdjustmentStatus> GetStatusAsync(long businessDayId, CancellationToken ct);

    Task<IReadOnlyList<CastSalesAdjustmentSlip>> GetSlipsAsync(long businessDayId, CancellationToken ct);

    Task<CastSalesAdjustmentDetail?> GetDetailAsync(long slipId, CancellationToken ct);

    Task<CastSalesAdjustmentSaveResult> SaveAsync(CastSalesAdjustmentSaveInput input, CancellationToken ct);

    Task<CastSalesAdjustmentSaveResult> SaveBatchAsync(
        long businessDayId,
        IReadOnlyCollection<CastSalesAdjustmentSaveInput> inputs,
        CancellationToken ct);
}
