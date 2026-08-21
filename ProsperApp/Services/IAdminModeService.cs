namespace ProsperApp.Services;

public interface IAdminModeService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
