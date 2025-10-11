using System;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Interfaces
{
    public interface IConfigurationService
    {
        AppSettings GetSettings();
        Task SaveSettingsAsync(AppSettings settings);
        Task SaveSettingsAsync(AppSettings settings, bool preserveLoginApiUrl);
        event EventHandler? ConfigurationChanged;
    }
}