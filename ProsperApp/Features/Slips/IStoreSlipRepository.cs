using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IStoreSlipRepository
{
    Task<StoreContext?> GetStoreContextAsync(CancellationToken ct);

    Task<IReadOnlyList<StoreTableOption>> GetTablesAsync(CancellationToken ct);

    Task<IReadOnlyList<CastOption>> GetCastsAsync(CancellationToken ct);

    Task<IReadOnlyList<BusinessSlipListItem>> GetBusinessDaySlipsAsync(long businessDayId, CancellationToken ct);

    Task<SlipDetail?> GetSlipDetailAsync(long slipId, CancellationToken ct);

    Task<CreateSlipResult> CreateSlipAsync(CreateSlipInputModel input, CancellationToken ct);

    Task<SlipMutationResult> AddSlipCustomersAsync(long slipId, IReadOnlyList<string?> customerLabels, DateTime enteredAt, CancellationToken ct);

    Task<SlipMutationResult> AddSlipNominationsAsync(long slipId, IReadOnlyList<CastNominationInputModel> nominations, CancellationToken ct);

    Task<SlipMutationResult> LeaveSlipCustomerAsync(long slipCustomerId, DateTime leftAt, CancellationToken ct);

    Task<SlipMutationResult> UpdateSlipCustomerLabelAsync(long slipCustomerId, string? customerLabel, CancellationToken ct);

    Task<SlipMutationResult> VoidOrderLineAsync(long orderLineId, CancellationToken ct);
}
