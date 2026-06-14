using ProsperApp.Models;

namespace ProsperApp.Services;

public interface ILocalSettingsProvider
{
    LocalSettings GetCurrent();
}
