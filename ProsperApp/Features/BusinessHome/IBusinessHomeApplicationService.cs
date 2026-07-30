using System.Text.Json;
using ProsperApp.Features.Shared;
using ProsperApp.Models;

namespace ProsperApp.Features.BusinessHome;

public interface IBusinessHomeApplicationService
{
    Task<Result<BusinessHomeSnapshotState>> GetSnapshotAsync(CancellationToken ct);

    Task<Result<BusinessHomeFlushOutput>> FlushAsync(
        BusinessHomeChangeFlushInput input,
        CancellationToken ct);
}

public sealed record BusinessHomeSnapshotState(
    StoreBusinessDay? BusinessDay,
    DateOnly BusinessDate,
    JsonElement? Snapshot);

public sealed record BusinessHomeFlushOutput(
    string BatchId,
    JsonElement Snapshot,
    JsonElement OperationResults,
    JsonElement KaraokeResults);
