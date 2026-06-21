using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IBusinessDayRepository
{
    Task<StoreBusinessDay?> GetCurrentAsync(CancellationToken ct);

    Task<BusinessDayEnsureResult> EnsureCurrentAsync(CancellationToken ct);

    Task<BusinessDayOperationResult> OpenAsync(
        DateOnly businessDate,
        string? memo,
        IReadOnlyCollection<BusinessDayAttendanceInput>? attendanceEntries,
        CancellationToken ct);

    Task<BusinessDayOperationResult> SaveAttendanceAsync(
        long businessDayId,
        IReadOnlyCollection<BusinessDayAttendanceInput> attendanceEntries,
        CancellationToken ct);

    Task<BusinessDayOperationResult> CloseAsync(long businessDayId, string? memo, CancellationToken ct);

    Task<int> GetOpenSlipCountAsync(long businessDayId, CancellationToken ct);

    Task<decimal> GetDrinkDeliveryAmountAsync(long businessDayId, CancellationToken ct);

    Task<BusinessDayDrinkDeliveryStatus> GetDrinkDeliveryStatusAsync(long businessDayId, CancellationToken ct);

    Task<BusinessDayAmountSaveResult> SaveDrinkDeliveryAmountAsync(long businessDayId, decimal amount, CancellationToken ct);

    Task<IReadOnlyList<BusinessDayClosingAttendanceItem>> GetClosingAttendanceAsync(long businessDayId, CancellationToken ct);

    Task<BusinessDayAttendanceSaveResult> SaveClosingAttendanceAsync(
        long businessDayId,
        IReadOnlyCollection<BusinessDayClosingAttendanceInput> attendanceEntries,
        CancellationToken ct);
}
