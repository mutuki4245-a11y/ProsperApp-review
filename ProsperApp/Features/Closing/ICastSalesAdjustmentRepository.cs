using ProsperApp.Models;

namespace ProsperApp.Services;

public interface ICastSalesAdjustmentRepository
{
    Task<CastSalesAdjustmentStatus> GetStatusAsync(long businessDayId, CancellationToken ct);

    Task<IReadOnlyList<CastSalesAdjustmentSlip>> GetSlipsAsync(long businessDayId, CancellationToken ct);

    Task<CastSalesAdjustmentDetail?> GetDetailAsync(long slipId, CancellationToken ct);

    Task<CastSalesAdjustmentSaveResult> SaveAsync(CastSalesAdjustmentSaveInput input, CancellationToken ct);
}
