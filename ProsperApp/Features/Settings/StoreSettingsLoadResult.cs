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

public class DebugDeleteNonMasterRecordsResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<DebugDeletedTableCount> TableCounts { get; init; } = [];

    public int DeletedCount => TableCounts.Sum(x => x.DeletedCount);

    public static DebugDeleteNonMasterRecordsResult Success(IReadOnlyList<DebugDeletedTableCount> tableCounts)
    {
        return new DebugDeleteNonMasterRecordsResult
        {
            Succeeded = true,
            TableCounts = tableCounts
        };
    }

    public static DebugDeleteNonMasterRecordsResult Failed(string message)
    {
        return new DebugDeleteNonMasterRecordsResult
        {
            Succeeded = false,
            ErrorMessage = message
        };
    }
}

public class DebugDeletedTableCount
{
    public string TableName { get; init; } = string.Empty;

    public int DeletedCount { get; init; }
}
