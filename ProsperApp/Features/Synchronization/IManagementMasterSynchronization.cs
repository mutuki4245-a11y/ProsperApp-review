using System.Text.Json;
using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Synchronization;

public interface IManagementMasterSynchronization
{
    Task<Result<ManagementMasterSnapshot>> GetSnapshotAsync(
        string? knownRevision,
        CancellationToken cancellationToken);

    Task<Result<ManagementMasterMutationResult>> MutateAsync(
        ManagementMasterMutation input,
        bool allowAdminActions,
        CancellationToken cancellationToken);
}

public sealed record ManagementMasterSnapshot(
    long DepartmentId,
    string Revision,
    bool Unchanged,
    JsonElement? Payload);

public sealed record ManagementMasterMutation(
    string OperationId,
    string Area,
    string Action,
    string ExpectedAreaRevision,
    JsonElement Payload);

public sealed record ManagementMasterMutationResult(
    string OperationId,
    string Status,
    string Revision,
    string AreaRevision,
    string Area,
    JsonElement Delta,
    string? ErrorCode,
    string? ErrorMessage);
