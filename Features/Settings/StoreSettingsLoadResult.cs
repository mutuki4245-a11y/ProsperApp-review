namespace ProsperApp.Models;

public class StoreSettingsLoadResult
{
    public IReadOnlyList<DepartmentOption> Departments { get; init; } = [];

    public string? DiagnosticMessage { get; init; }

    public string? RpcStatus { get; init; }

    public string? TableStatus { get; init; }

    public bool Succeeded => Departments.Count > 0;

    public static StoreSettingsLoadResult Success(
        IReadOnlyList<DepartmentOption> departments,
        string? rpcStatus = null,
        string? tableStatus = null)
    {
        return new StoreSettingsLoadResult
        {
            Departments = departments,
            RpcStatus = rpcStatus,
            TableStatus = tableStatus
        };
    }

    public static StoreSettingsLoadResult Failed(
        string diagnosticMessage,
        string? rpcStatus = null,
        string? tableStatus = null)
    {
        return new StoreSettingsLoadResult
        {
            DiagnosticMessage = diagnosticMessage,
            RpcStatus = rpcStatus,
            TableStatus = tableStatus
        };
    }
}
