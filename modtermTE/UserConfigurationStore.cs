using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public IReadOnlyList<string> GetThemeNames()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in Directory.GetFiles(UserConfigDirectory, "theme_*.json"))
            {
                names.Add(Path.GetFileNameWithoutExtension(path).Substring(6));
            }

            string bundledThemesDirectory = GetBundledDefaultThemesDirectory();
            if (Directory.Exists(bundledThemesDirectory))
            {
                foreach (string path in Directory.GetFiles(bundledThemesDirectory, "theme_*.json"))
                {
                    names.Add(Path.GetFileNameWithoutExtension(path).Substring(6));
                }
            }

            return names.ToList();
        }

        public ThemeConfiguration LoadTheme(string themeName)
        {
            string fileName = $"theme_{themeName}.json";
            string userThemePath = Path.Combine(UserConfigDirectory, fileName);
            string themePath = File.Exists(userThemePath)
                ? userThemePath
                : Path.Combine(GetBundledDefaultThemesDirectory(), fileName);

            if (!File.Exists(themePath))
            {
                throw new FileNotFoundException($"Theme file not found: {fileName}", themePath);
            }

            var theme = JsonSerializer.Deserialize<ThemeConfiguration>(File.ReadAllText(themePath));
            if (theme is null)
            {
                throw new InvalidDataException($"Theme file deserialized to null: {fileName}");
            }

            theme.Name = themeName;
            return theme;
        }

        private static string GetBundledDefaultThemesDirectory()
            => Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultThemes");
    }
}
