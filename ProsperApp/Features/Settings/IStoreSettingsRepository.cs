
namespace ProsperApp.Features.Settings;

public interface IStoreSettingsRepository
{
    Task<StoreSettingsLoadResult> GetDepartmentsAsync(CancellationToken ct);

    Task<DebugDeleteNonMasterRecordsResult> DeleteNonMasterRecordsAsync(long departmentId, CancellationToken ct);
}
