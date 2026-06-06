using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IBusinessDayRepository
{
    Task<StoreBusinessDay?> GetCurrentAsync(CancellationToken ct);

    Task<BusinessDayOperationResult> OpenAsync(DateOnly businessDate, string? memo, CancellationToken ct);

    Task<BusinessDayOperationResult> CloseAsync(long businessDayId, string? memo, CancellationToken ct);

    Task<int> GetOpenSlipCountAsync(long businessDayId, CancellationToken ct);
}
