using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Closing;

public sealed class ClosingApplicationService(
    IBusinessDayRepository businessDayRepository) : IClosingApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;

    public Task<Result<CurrentClosingDashboard>> GetDashboardAsync(
        string? knownCastMasterRevision,
        CancellationToken ct)
    {
        return _businessDayRepository.GetCurrentClosingDashboardAsync(knownCastMasterRevision, ct);
    }

    public Task<Result<CurrentBusinessDayCloseOutput>> CloseAsync(
        CurrentBusinessDayCloseMutation input,
        CancellationToken ct)
    {
        return _businessDayRepository.CloseCurrentBusinessDayAsync(input, ct);
    }
}
