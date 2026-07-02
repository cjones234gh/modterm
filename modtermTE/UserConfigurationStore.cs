using System;
using System.IO;
using System.Text.Json;
using Windows.Foundation;

namespace modtermTE
{
    internal sealed class UserConfigurationStore
    {
        public string UserConfigDirectory { get; }
        public string UserAppConfigPath { get; }

        public UserConfigurationStore()
        {
            UserConfigDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "modterm");
            Directory.CreateDirectory(UserConfigDirectory);
            UserAppConfigPath = Path.Combine(UserConfigDirectory, "userAppConfig.json");
        }

        public void Save(UserAppConfiguration configuration)
        {
            string json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UserAppConfigPath, json);
        }

        public void SaveTheme(ThemeConfiguration themeConfiguration)
        {
            string themePath = Path.Combine(UserConfigDirectory, $"theme_{themeConfiguration.Name}.json");
            string themeJson = JsonSerializer.Serialize(themeConfiguration, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(themePath, themeJson);
        }

        public UserAppConfiguration LoadConfig()
        {
            string json = File.ReadAllText(UserAppConfigPath);
            return JsonSerializer.Deserialize<UserAppConfiguration>(json);
        }

        public ThemeConfiguration LoadTheme(string themeName)
        {
            string themePath = Path.Combine(UserConfigDirectory, $"theme_{themeName}.json");
            return JsonSerializer.Deserialize<ThemeConfiguration>(File.ReadAllText(themePath));
        }
    }
}
