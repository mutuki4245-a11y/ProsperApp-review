namespace ProsperApp.Features.Catalog;

public static class StoreStaffEmploymentTypes
{
    public const string Employee = "employee";

    public const string PartTime = "part_time";

    public static bool IsValid(string? value) =>
        value is Employee or PartTime;

    public static string Normalize(string? value) =>
        value is PartTime ? PartTime : Employee;
}

public class StaffOption
{
    public long StaffId { get; set; }

    public string? StaffCode { get; set; }

    public string? DepartmentName { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string EmploymentType { get; set; } = StoreStaffEmploymentTypes.Employee;

    public string SearchDisplayName => string.IsNullOrWhiteSpace(DepartmentName)
        ? DisplayName
        : $"{DisplayName}：{DepartmentName}";
}
