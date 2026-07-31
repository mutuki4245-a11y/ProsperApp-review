using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Catalog;

public interface IStoreStaffAdminRepository
{
    Task<Result<IReadOnlyList<StaffOption>>> GetStaffOptionsAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreStaffAdminItem>>> GetStaffsAsync(CancellationToken ct);

    Task<StoreStaffSaveResult> CreateStaffAsync(StoreStaffCreateInputModel input, CancellationToken ct);

    Task<StoreStaffSaveResult> DeleteStaffAsync(long staffId, CancellationToken ct);
}
