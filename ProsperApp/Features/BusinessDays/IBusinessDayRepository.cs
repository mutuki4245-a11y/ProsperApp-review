using ProsperApp.Features.Shared;

namespace ProsperApp.Features.BusinessDays;

public interface IBusinessDayRepository
{
    Task<Result<AttendanceShellBootstrap>> GetAttendanceShellAsync(CancellationToken ct);

    Task<Result<AttendanceEditorSnapshot>> GetCurrentAttendanceEditorAsync(
        long? knownBusinessDayId,
        long? knownBusinessDayRevision,
        CancellationToken ct);

    Task<Result<CurrentBusinessDayAttendanceSaveOutput>> SaveCurrentAttendanceAsync(
        CurrentBusinessDayAttendanceMutation input,
        CancellationToken ct);

    Task<Result<CurrentBusinessDayCloseOutput>> CloseCurrentBusinessDayAsync(
        CurrentBusinessDayCloseMutation input,
        CancellationToken ct);

    Task<Result<CurrentClosingDashboard>> GetCurrentClosingDashboardAsync(
        string? knownCastMasterRevision,
        CancellationToken ct);

    Task<Result<CurrentDrinkDeliveryEditorState>> GetCurrentDrinkDeliveryEditorAsync(CancellationToken ct);

    Task<Result<CurrentBusinessDayDrinkDeliverySaveOutput>> SaveCurrentDrinkDeliveryAmountAsync(
        CurrentBusinessDayDrinkDeliveryMutation input,
        CancellationToken ct);

}
