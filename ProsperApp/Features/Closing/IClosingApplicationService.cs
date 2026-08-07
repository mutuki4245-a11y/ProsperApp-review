using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Closing;

public interface IClosingApplicationService
{
    Task<Result<CurrentClosingDashboard>> GetDashboardAsync(
        string? knownCastMasterRevision,
        CancellationToken ct);

    Task<Result<CurrentBusinessDayCloseOutput>> CloseAsync(
        CurrentBusinessDayCloseMutation input,
        CancellationToken ct);
}
