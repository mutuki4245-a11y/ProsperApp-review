namespace ProsperApp.Features.Admin;

public interface IAdminAuthorizationService
{
    bool IsAdminMode { get; }

    void SetAdminMode(bool enabled);
}
