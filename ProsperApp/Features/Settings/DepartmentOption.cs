namespace ProsperApp.Models;

public class DepartmentOption
{
    public long DepartmentId { get; set; }

    public long CompanyId { get; set; }

    public string? DepartmentCode { get; set; }

    public string DepartmentName { get; set; } = string.Empty;

    public string DisplayName => DepartmentName;
}
