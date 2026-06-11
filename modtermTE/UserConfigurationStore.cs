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

        public UserAppConfiguration LoadOrDefault(out bool loadedFromDisk)
        {
            if (!File.Exists(UserAppConfigPath))
            {
                loadedFromDisk = false;
                return DefaultUserConfiguration.Create();
            }

            if (TryLoad(out var configuration))
            {
                loadedFromDisk = true;
                return configuration;
            }

            loadedFromDisk = false;
            return DefaultUserConfiguration.Create();
        }

        public bool TryLoad(out UserAppConfiguration configuration)
        {
            try
            {
                string json = File.ReadAllText(UserAppConfigPath);
                var loadedConfiguration = JsonSerializer.Deserialize<UserAppConfiguration>(json);
                MigrateLegacyWindowLocation(json, loadedConfiguration);
                configuration = ValidateLoadedConfiguration(loadedConfiguration);
                return true;
            }
            catch
            {
                configuration = DefaultUserConfiguration.Create();
                return false;
            }
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

        private static void MigrateLegacyWindowLocation(string json, UserAppConfiguration? configuration)
        {
            if (configuration is null || json.Contains("\"WindowSize\"", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("WindowLocation", out var windowLocation))
                {
                    return;
                }

                configuration.LastWindowLocation = new Point(
                    windowLocation.GetProperty("X").GetDouble(),
                    windowLocation.GetProperty("Y").GetDouble());
                configuration.WindowSize = new Size(
                    windowLocation.GetProperty("Width").GetDouble(),
                    windowLocation.GetProperty("Height").GetDouble());
            }
            catch
            {
                // Keep defaults from deserialization when legacy data is malformed.
            }
        }

        private static UserAppConfiguration ValidateLoadedConfiguration(UserAppConfiguration? configuration)
        {
            if (configuration is null)
            {
                throw new InvalidDataException("User configuration deserialized to null.");
            }

            if (configuration.TerminalShell is null)
            {
                throw new InvalidDataException("Terminal shell is missing.");
            }

            if (configuration.ThemeConfiguration is null)
            {
                throw new InvalidDataException("Theme configuration is missing.");
            }

            if (configuration.ShellConfigurations is null)
            {
                throw new InvalidDataException("Shell configurations are missing.");
            }

            return configuration;
        }
    }
}
