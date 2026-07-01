using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;

namespace modtermTE
{
    public sealed partial class ModtermTeWindow
    {
        private readonly UserConfigurationStore _configurationStore = new();
        private UserAppConfiguration _configuration = null!;
        private ComboBox? _terminalFontCombo;
        private ComboBox? _labelFontCombo;
        private ComboBox? _shellCombo;
        private NumberBox? _blurAmountBox;
        private Slider? _opacitySlider;
        private TextBlock? _opacityValueText;
        private ComboBox? _backdropCombo;
        private StackPanel? _palettePanel;
        private TextBlock? _themeHeader = null!;
        private bool _settingsUiReady;

        private void InitializeSettings()
        {
            _configuration = _configurationStore.LoadOrDefault(out var loadedFromDisk);
            if (!loadedFromDisk && File.Exists(_configurationStore.UserAppConfigPath))
            {
                _ = ShowSimpleDialogAsync(
                    "Configuration Load Failed",
                    "Your configuration failed to load. Using defaults; save when ready to write a new file.");
            }

            BuildSettingsUi();
        }

        private void BuildSettingsUi()
        {
            _settingsUiReady = false;
            SettingsPanel.Children.Clear();

            SettingsPanel.Children.Add(CreateSectionHeader("Terminal"));
            SettingsPanel.Children.Add(CreateSettingRow("Terminal Font", CreateTerminalFontCombo()));
            SettingsPanel.Children.Add(CreateSettingRow("Label Font", CreateLabelFontCombo()));
            SettingsPanel.Children.Add(CreateSettingRow("Shell", CreateShellCombo()));
            SettingsPanel.Children.Add(CreateSettingRow("Cursor", CreateCursorDisplay()));

            _themeHeader = CreateSectionHeader("Theme");
            SettingsPanel.Children.Add(_themeHeader);
            var theme = _configuration.ThemeConfiguration;
            SettingsPanel.Children.Add(CreateSettingRow("Output Color",
                CreateColorPickerButton(theme.OutputColor, color => theme.OutputColor = color)));
            SettingsPanel.Children.Add(CreateSettingRow("Blur Color",
                CreateColorPickerButton(theme.OutputBlurColor, color => theme.OutputBlurColor = color)));
            SettingsPanel.Children.Add(CreateSettingRow("Label Color",
                CreateColorPickerButton(theme.LabelColor, color => theme.LabelColor = color)));
            SettingsPanel.Children.Add(CreateSettingRow("Label Blur Color",
                CreateColorPickerButton(theme.LabelBlurColor, color => theme.LabelBlurColor = color)));
            SettingsPanel.Children.Add(CreateSettingRow("Blur Amount", CreateBlurAmountBox(theme.BlurAmount)));
            SettingsPanel.Children.Add(CreateSettingRow("Opacity", CreateOpacitySlider(theme.WindowOpacityPct)));
            SettingsPanel.Children.Add(CreateSettingRow("Window Color",
                CreateColorPickerButton(theme.WindowColor, color => theme.WindowColor = color)));
            SettingsPanel.Children.Add(CreateSettingRow("Backdrop", CreateBackdropCombo(theme.BackdropKind)));

            // Palette Mapping section using modern WinUI Expander for collapsible palette editor
            SettingsPanel.Children.Add(CreateSectionHeader("Palette Mapping"));
            SettingsPanel.Children.Add(CreatePaletteMappingControl(theme));

            _settingsUiReady = true;
        }

        private void NotifyConfigurationChanged()
        {
            if (!_settingsUiReady || _liveConfigurationPublisher is null)
            {
                return;
            }

            _liveConfigurationPublisher.SchedulePublish(_configuration);
        }

        private ComboBox CreateTerminalFontCombo()
        {
            _terminalFontCombo = CreateFontCombo(_configuration.TerminalFont, font =>
            {
                _configuration.TerminalFont = font;
                NotifyConfigurationChanged();
            });
            return _terminalFontCombo;
        }

        private ComboBox CreateLabelFontCombo()
        {
            _labelFontCombo = CreateFontCombo(_configuration.LabelFont, font =>
            {
                _configuration.LabelFont = font;
                NotifyConfigurationChanged();
            });
            return _labelFontCombo;
        }

        private ComboBox CreateFontCombo(string selectedFont, Action<string> onSelected)
        {
            var fonts = MonospaceFontProvider.GetFontFamilyNames().ToList();
            if (!string.IsNullOrWhiteSpace(selectedFont)
                && !fonts.Any(f => string.Equals(f, selectedFont, StringComparison.OrdinalIgnoreCase)))
            {
                fonts.Insert(0, selectedFont);
            }

            var combo = new ComboBox
            {
                ItemsSource = fonts,
                SelectedItem = fonts.FirstOrDefault(f => string.Equals(f, selectedFont, StringComparison.OrdinalIgnoreCase)) ?? selectedFont,
                PlaceholderText = selectedFont,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 360
            };

            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string fontName)
                {
                    onSelected(fontName);
                }
            };

            return combo;
        }

        private ComboBox CreateShellCombo()
        {
            _shellCombo = new ComboBox
            {
                ItemsSource = _configuration.ShellConfigurations,
                DisplayMemberPath = nameof(Shell.Name),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 360
            };

            _shellCombo.SelectedItem = _configuration.ShellConfigurations
                .FirstOrDefault(shell => string.Equals(shell.Name, _configuration.TerminalShell.Name, StringComparison.OrdinalIgnoreCase))
                ?? _configuration.TerminalShell;

            _shellCombo.SelectionChanged += (_, _) =>
            {
                if (_shellCombo.SelectedItem is Shell shell)
                {
                    _configuration.TerminalShell = shell;
                    NotifyConfigurationChanged();
                }
            };

            return _shellCombo;
        }

        private TextBlock CreateCursorDisplay()
        {
            return new TextBlock
            {
                Text = "bar",
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.8
            };
        }

        private NumberBox CreateBlurAmountBox(float initialValue)
        {
            _blurAmountBox = new NumberBox
            {
                Value = initialValue,
                SmallChange = 0.5,
                LargeChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                AcceptsExpression = false,
                Minimum = 0,
                Maximum = 100,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 200
            };

            _blurAmountBox.ValueChanged += (_, args) =>
            {
                if (args.NewValue is double value)
                {
                    _configuration.ThemeConfiguration.BlurAmount = (float)value;
                    NotifyConfigurationChanged();
                }
            };

            return _blurAmountBox;
        }

        private Grid CreateOpacitySlider(int initialValue)
        {
            _opacitySlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                StepFrequency = 1,
                Value = initialValue,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Width = 240
            };

            _opacityValueText = new TextBlock
            {
                Text = $"{initialValue}%",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 40,
                Margin = new Thickness(12, 0, 0, 0)
            };

            _opacitySlider.ValueChanged += (_, _) =>
            {
                int value = (int)_opacitySlider.Value;
                _configuration.ThemeConfiguration.WindowOpacityPct = value;
                _opacityValueText.Text = $"{value}%";
                NotifyConfigurationChanged();
            };

            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 360 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_opacitySlider, 0);
            Grid.SetColumn(_opacityValueText, 1);
            grid.Children.Add(_opacitySlider);
            grid.Children.Add(_opacityValueText);
            return grid;
        }

        private ComboBox CreateBackdropCombo(BackdropKind initialValue)
        {
            var backdropOptions = new List<BackdropOption>
            {
                new(BackdropKind.Mica, "Mica"),
                new(BackdropKind.Acrylic, "Acrylic"),
                new(BackdropKind.Blurred, "Blurred")
            };

            _backdropCombo = new ComboBox
            {
                ItemsSource = backdropOptions,
                DisplayMemberPath = nameof(BackdropOption.Label),
                SelectedItem = backdropOptions.First(option => option.Kind == initialValue),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 360
            };

            _backdropCombo.SelectionChanged += (_, _) =>
            {
                if (_backdropCombo.SelectedItem is BackdropOption option)
                {
                    _configuration.ThemeConfiguration.BackdropKind = option.Kind;
                    NotifyConfigurationChanged();
                }
            };

            return _backdropCombo;
        }

        private Button CreateColorPickerButton(Color initialColor, Action<Color> onColorChanged)
        {
            var preview = new Border
            {
                Width = 36,
                Height = 28,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.4 },
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(initialColor)
            };

            var picker = new ColorPicker
            {
                Color = initialColor,
                IsAlphaEnabled = true,
                IsColorSpectrumVisible = true,
                IsColorChannelTextInputVisible = true,
                IsColorSliderVisible = true,
                IsHexInputVisible = true
            };

            picker.ColorChanged += (_, args) =>
            {
                onColorChanged(args.NewColor);
                preview.Background = new SolidColorBrush(args.NewColor);
                NotifyConfigurationChanged();
            };

            var button = new Button
            {
                Content = preview,
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var flyout = new Flyout { Content = picker };
            button.Flyout = flyout;
            return button;
        }

        private TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                Margin = new Thickness(0, 20, 0, 8)
            };
        }

        private Border CreateSettingRow(string label, FrameworkElement control)
        {
            var grid = new Grid
            {
                MinHeight = 48,
                Padding = new Thickness(0, 10, 0, 10),
                ColumnSpacing = 24
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            };
            Grid.SetColumn(labelBlock, 0);

            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);

            grid.Children.Add(labelBlock);
            grid.Children.Add(control);

            return new Border
            {
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.15 },
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };
        }

        private void SaveConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            _liveConfigurationPublisher?.PublishNow(_configuration);
            _ = ShowSimpleDialogAsync("Configuration Saved", "Your settings were saved to userAppConfig.json and applied to modterm.");
        }

        private async void SaveAsNewThemeButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentLookAsNewThemeAsync();
        }

        private async Task SaveCurrentLookAsNewThemeAsync()
        {
            if (RootGrid.XamlRoot is null)
            {
                return;
            }

            var nameBox = new TextBox
            {
                PlaceholderText = "Theme name",
                AcceptsReturn = false
            };

            var dialog = new ContentDialog
            {
                Title = "Make Current Look a New Theme",
                Content = nameBox,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            string themeName = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(themeName))
            {
                return;
            }

            if (themeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                await ShowSimpleDialogAsync("Invalid Theme Name", "Theme name contains characters that cannot be used in a file name.");
                return;
            }

            string themeJson = JsonSerializer.Serialize(_configuration.ThemeConfiguration);
            var themeConfig = JsonSerializer.Deserialize<ThemeConfiguration>(themeJson);
            if (themeConfig is null)
            {
                return;
            }

            themeConfig.Name = themeName;
            _configurationStore.SaveTheme(themeConfig);
            await ShowSimpleDialogAsync("Theme Saved", $"Theme \"{themeName}\" was saved.");
        }

        private async Task ShowSimpleDialogAsync(string title, string content)
        {
            if (RootGrid.XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private sealed record BackdropOption(BackdropKind Kind, string Label);

        private static Color GetDefaultAnsiColor(string name)
        {
            return name switch
            {
                "Black" => Color.FromArgb(255, 12, 12, 12),
                "Red" => Color.FromArgb(255, 197, 15, 21),
                "Green" => Color.FromArgb(255, 19, 161, 14),
                "Yellow" => Color.FromArgb(255, 193, 156, 0),
                "Blue" => Color.FromArgb(255, 0, 55, 218),
                "Magenta" => Color.FromArgb(255, 136, 23, 152),
                "Cyan" => Color.FromArgb(255, 58, 150, 221),
                "White" => Color.FromArgb(255, 204, 204, 204),
                "BrightBlack" => Color.FromArgb(255, 118, 118, 118),
                "BrightRed" => Color.FromArgb(255, 231, 72, 86),
                "BrightGreen" => Color.FromArgb(255, 22, 198, 12),
                "BrightYellow" => Color.FromArgb(255, 249, 241, 165),
                "BrightBlue" => Color.FromArgb(255, 59, 120, 255),
                "BrightMagenta" => Color.FromArgb(255, 180, 0, 158),
                "BrightCyan" => Color.FromArgb(255, 97, 214, 214),
                "BrightWhite" => Color.FromArgb(255, 242, 242, 242),
                _ => Microsoft.UI.Colors.Gray
            };
        }

        /// <summary>
        /// Creates a modern WinUI Expander containing the Palette Mapping interface.
        /// Left: fixed default ANSI color name + square swatch. Right: color picker (shows current override, updates live).
        /// Updates live and supports serialization of the Dictionary in ThemeConfiguration.
        /// </summary>
        private FrameworkElement CreatePaletteMappingControl(ThemeConfiguration theme)
        {
            if (theme.Palette is null)
            {
                theme.Palette = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            }
            // Ensure all 16 keys exist (merges loaded theme values with any missing defaults). This prevents
            // all-black initialization and ensures color pickers start with the correct current value.
            string[] standardNames = {
                "Black", "Red", "Green", "Yellow", "Blue", "Magenta", "Cyan", "White",
                "BrightBlack", "BrightRed", "BrightGreen", "BrightYellow",
                "BrightBlue", "BrightMagenta", "BrightCyan", "BrightWhite"
            };
            foreach (var name in standardNames)
            {
                if (!theme.Palette.ContainsKey(name))
                {
                    theme.Palette[name] = GetDefaultAnsiColor(name);
                }
            }

            _palettePanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

            string[] paletteNames = {
                "Black", "Red", "Green", "Yellow", "Blue", "Magenta", "Cyan", "White",
                "BrightBlack", "BrightRed", "BrightGreen", "BrightYellow",
                "BrightBlue", "BrightMagenta", "BrightCyan", "BrightWhite"
            };
            for (int i = 0; i < paletteNames.Length; i++)
            {
                string name = paletteNames[i];
                _palettePanel.Children.Add(CreatePaletteRow(theme, i, name));
            }

            var expander = new Expander
            {
                Header = "Terminal ANSI Palette Overrides",
                Content = _palettePanel,
                IsExpanded = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 16)
            };

            return expander;
        }

        private FrameworkElement CreatePaletteRow(ThemeConfiguration theme, int index, string name)
        {
            var grid = new Grid
            {
                MinHeight = 48,
                Padding = new Thickness(0, 8, 0, 8),
                ColumnSpacing = 16
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) }); // Label + swatch area
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Color picker

            // Left side: Name label and color swatch
            var leftPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameText = new TextBlock
            {
                Text = name,
                Width = 100,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            };

            // Fixed default ANSI color swatch (for visual reference); picker on right shows/sets the override
            Color currentColor = theme.Palette!.TryGetValue(name, out Color c) ? c : GetDefaultAnsiColor(name);
            Color defaultAnsiColor = GetDefaultAnsiColor(name);
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.6 },
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(defaultAnsiColor),
                VerticalAlignment = VerticalAlignment.Center
            };

            leftPanel.Children.Add(nameText);
            leftPanel.Children.Add(swatch);
            Grid.SetColumn(leftPanel, 0);

            // Right side: Color picker button (reuses existing helper logic)
            var pickerButton = CreateColorPickerButton(
                currentColor,
                color =>
                {
                    if (theme.Palette is null)
                    {
                        theme.Palette = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
                    }
                    theme.Palette[name] = color;
                    NotifyConfigurationChanged();
                });

            Grid.SetColumn(pickerButton, 1);

            grid.Children.Add(leftPanel);
            grid.Children.Add(pickerButton);

            return new Border
            {
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.1 },
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };
        }
    }
}
