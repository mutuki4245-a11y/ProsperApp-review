using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Closing;

public interface IChampagneBackRepository
{
    Task<Result<ChampagneBackOverview>> GetOverviewAsync(long businessDayId, CancellationToken ct);

    Task<ChampagneBackSaveResult> SaveAsync(ChampagneBackSaveInput input, CancellationToken ct);
}
