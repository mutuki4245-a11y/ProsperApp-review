using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IStoreSettingsRepository
{
    Task<StoreSettingsLoadResult> GetDepartmentsAsync(CancellationToken ct);
}
