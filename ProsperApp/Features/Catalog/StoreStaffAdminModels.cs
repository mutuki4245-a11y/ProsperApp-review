using System.ComponentModel.DataAnnotations;

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

public class StoreStaffAdminItem
{
    public long StaffId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public DateOnly JoinedOn { get; set; }

    public string EmploymentType { get; set; } = StoreStaffEmploymentTypes.Employee;
}

public class StoreStaffCreateInputModel
{
    [Display(Name = "スタッフ名")]
    [Required(ErrorMessage = "スタッフ名を入力してください。")]
    [StringLength(120, ErrorMessage = "スタッフ名は120文字以内で入力してください。")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "雇用区分")]
    [Required(ErrorMessage = "雇用区分を選択してください。")]
    [RegularExpression("^(employee|part_time)$", ErrorMessage = "雇用区分を選択してください。")]
    public string EmploymentType { get; set; } = StoreStaffEmploymentTypes.Employee;
}

public class StoreStaffSaveResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public long? StaffId { get; init; }

    public static StoreStaffSaveResult Success(long staffId)
    {
        return new StoreStaffSaveResult { Succeeded = true, StaffId = staffId };
    }

    public static StoreStaffSaveResult Failed(string message)
    {
        return new StoreStaffSaveResult { Succeeded = false, ErrorMessage = message };
    }
}
