using ProsperApp.Features.Shared;

namespace ProsperApp.Features.BusinessDays;

public interface IBusinessDayRepository
{
    Task<Result<StoreBusinessDay?>> GetCurrentAsync(CancellationToken ct, bool forceRefresh = false);

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

    Task<BusinessDayOperationResult> CloseAsync(
        long businessDayId,
        string? memo,
        bool includePendingReceipts,
        bool ignoreClosingRequirements,
        CancellationToken ct);

    Task<Result<BusinessDayClosingReadiness>> GetClosingReadinessAsync(
        StoreBusinessDay businessDay,
        bool includePendingReceipts,
        CancellationToken ct);

    Task<Result<int>> GetOpenSlipCountAsync(long businessDayId, CancellationToken ct);

    Task<Result<BusinessDayDrinkDeliveryStatus>> GetDrinkDeliveryStatusAsync(long businessDayId, CancellationToken ct);

    Task<BusinessDayAmountSaveResult> SaveDrinkDeliveryAmountAsync(long businessDayId, decimal amount, CancellationToken ct);

    Task<Result<IReadOnlyList<BusinessDayClosingAttendanceItem>>> GetClosingAttendanceAsync(long businessDayId, CancellationToken ct);

    Task<BusinessDayAttendanceSaveResult> SaveClosingAttendanceAsync(
        long businessDayId,
        IReadOnlyCollection<BusinessDayClosingAttendanceInput> attendanceEntries,
        CancellationToken ct);
}
