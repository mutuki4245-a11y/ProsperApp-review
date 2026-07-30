using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Catalog;

public interface INominationBackAdminRepository
{
    Task<Result<IReadOnlyList<NominationBackMasterItem>>> GetSettingsAsync(CancellationToken ct);

    Task<NominationBackMasterSaveResult> SaveSettingsAsync(
        IReadOnlyList<NominationBackMasterInputModel> settings,
        CancellationToken ct);
}
