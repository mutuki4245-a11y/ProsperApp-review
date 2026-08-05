using System.Text.Json;
using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Synchronization;

public interface IManagementMasterSynchronization
{
    Task<Result<ManagementMasterSnapshot>> GetSnapshotAsync(
        string? knownRevision,
        CancellationToken cancellationToken);
}

public sealed record ManagementMasterSnapshot(
    long DepartmentId,
    string Revision,
    bool Unchanged,
    JsonElement? Payload);
