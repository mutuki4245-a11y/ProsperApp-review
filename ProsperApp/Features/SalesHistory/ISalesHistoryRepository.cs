using ProsperApp.Features.Shared;

namespace ProsperApp.Features.SalesHistory;

public interface ISalesHistoryRepository
{
    Task<Result<SalesHistoryPage>> GetPageAsync(
        SalesHistoryQuery query,
        DateOnly currentBusinessDate,
        CancellationToken ct);

    Task<Result<SalesHistoryCorrectionEditor>> GetCorrectionEditorAsync(
        long businessDayId,
        DateOnly currentBusinessDate,
        CancellationToken ct);

    Task<Result<SalesHistoryCorrectionResult>> SaveCorrectionAsync(
        SalesHistoryCorrectionMutation mutation,
        CancellationToken ct);
}
