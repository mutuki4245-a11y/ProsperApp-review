using System.Text.Json;
using ProsperApp.Features.Shared;

namespace ProsperApp.Features.StoreBootstrap;

public interface IStoreMasterBootstrapper
{
    Task<Result<StoreBootstrapPayload>> EnsureAsync(CancellationToken ct);
}

public sealed record StoreBootstrapPayload(long DepartmentId, JsonElement Row, bool WasFetched);
