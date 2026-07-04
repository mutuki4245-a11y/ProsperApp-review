using ProsperApp.Models;

namespace ProsperApp.Services;

public interface INominationBackAdminRepository
{
    Task<IReadOnlyList<NominationBackMasterItem>> GetSettingsAsync(CancellationToken ct);

    Task<NominationBackMasterSaveResult> SaveSettingsAsync(
        IReadOnlyList<NominationBackMasterInputModel> settings,
        CancellationToken ct);
}
