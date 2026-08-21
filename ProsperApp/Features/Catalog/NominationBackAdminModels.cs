namespace ProsperApp.Features.Catalog;

public sealed class NominationBackMasterItem
{
    public string NominationKind { get; set; } = string.Empty;

    public string NominationType { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? CompanionTime { get; set; }

    public string BackType { get; set; } = "nomination";

    public decimal BackUnitAmount { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
