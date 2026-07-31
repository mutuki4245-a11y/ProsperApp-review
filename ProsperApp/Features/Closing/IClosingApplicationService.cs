using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Closing;

public interface IClosingApplicationService
{
    Task<Result<ClosingPageState>> LoadAsync(
        bool includeReadiness,
        bool includePendingReceipts,
        bool forceRefresh,
        CancellationToken ct);

    Task<Result<StoreBusinessDay>> CloseAsync(
        long? submittedBusinessDayId,
        string? memo,
        bool includePendingReceipts,
        bool ignoreClosingRequirements,
        CancellationToken ct);
}

public sealed record ClosingPageState(
    StoreBusinessDay? BusinessDay,
    BusinessDayClosingReadiness? Readiness,
    DateTimeOffset? LastUpdatedAt);
