
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Windows.Foundation;
using System.Linq;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI;
using Windows.UI;
using Windows.Graphics;
using System.ComponentModel;
using System.Diagnostics;

namespace modterm
{
    public sealed partial class ModtermWindow : Window
    {
        // main terminal logic and state
        public ConPTYTerminal ConPtyTerminal { get; private set; } = null!;
        private DispatcherQueueTimer _resizeStopTimer = null!;

        // user storage for configs, themes, etc.
        private string _userConfigDirectory = string.Empty;
        private string _userAppConfigPath = string.Empty;

        // user app configuration
        private UserAppConfiguration _uac = null!;
        private bool _saveConfiguration = true;
        private bool _showConfigLoadFailureDialog = false;

        // theme names from config directory
        private List<string> _themeNames = new List<string>();

        // modterm render
        private ModtermRender _mtr = new ModtermRender();

        // live reload from modtermTE
        private ConfigurationReloadListener? _configurationReloadListener;
        private DispatcherQueueTimer? _configurationReloadTimer;

        // context menu flyout for right-click
        private MenuFlyout _flyout = null!;
        private bool _terminalRestartInProgress = false;

        public void InvalidateModtermCanvas()
        {
            ModtermCanvas.Invalidate();
        }

        public ConPTYTerminal EnsureTerminalInstanceForStart()
        {
            if (ConPtyTerminal.IsDisposed)
            {
                var previousTerminal = ConPtyTerminal;
                ConPtyTerminal = CreateTerminalInstance();
                DisposeTerminalInstance(previousTerminal);
            }

            return ConPtyTerminal;
        }

        private void InitializeApplication()
        {
            // wait for debugger to attach
            // while (!Debugger.IsAttached)
            // {
            //     Task.Delay(100).Wait();
            // }
            _flyout = new MenuFlyout();
            RootGrid.Loaded += RootGrid_Loaded;

            _resizeStopTimer = DispatcherQueue.CreateTimer();
            

            _userConfigDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "modterm");
            Directory.CreateDirectory(_userConfigDirectory);
            _userAppConfigPath = Path.Combine(_userConfigDirectory, "userAppConfig.json");

            if (File.Exists(_userAppConfigPath))
            {
                if (TryLoadUserConfiguration(out var loadedConfiguration))
                {
                    SetUserConfiguration(loadedConfiguration);
                }
                else
                {
                    _saveConfiguration = false;
                    _showConfigLoadFailureDialog = true;
                    SetUserConfiguration(GetDefaultAppConfiguration());
                }
            }
            else
            {
                CopyDefaultThemeFiles(overwriteExisting: false);
                SetUserConfiguration(GetDefaultAppConfiguration());
                WriteConfigurationToDisk(_uac);
            }

            LoadThemeNames();

            // init modterm render and set default appearance config
            _mtr = new ModtermRender();
            _mtr.ModtermWinInstance = this;
            _mtr.UserAppConfiguration = _uac;
            _mtr.Initialize();
            
            ApplyCurrentUserConfiguration(applyWindowBounds: true);

            ConPtyTerminal = CreateTerminalInstance();

            // window setup
            this.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            this.SetTitleBar(AppTitleBar);

            this.SizeChanged += MainWindow_SizeChanged;
            this.Closed += (s, e) =>
            {
                _configurationReloadListener?.Dispose();
                _configurationReloadListener = null;
                PersistLastWindowLocation();
                SaveConfig();
                DisposeTerminalInstance(ConPtyTerminal);
            };

            _configurationReloadListener = new ConfigurationReloadListener(
                DispatcherQueue,
                RequestConfigurationReload);

            RootGrid.KeyDown += ModtermCanvas_KeyDown;

            ModtermCanvas.Draw += _mtr.ModtermCanvas_Draw;
            

            // Mouse support
            ModtermCanvas.PointerWheelChanged += this.ModtermCanvas_PointerWheelChanged;
            ModtermCanvas.PointerPressed += this.ModtermCanvas_PointerPressed;
            ModtermCanvas.PointerMoved += this.ModtermCanvas_PointerMoved;
            ModtermCanvas.PointerReleased += this.ModtermCanvas_PointerReleased;
            ModtermCanvas.RightTapped += this.ModtermCanvas_RightTapped;

            // Debounce a burst of size events, then apply a live resize once they settle.
            _resizeStopTimer.Interval = TimeSpan.FromMilliseconds(120);
            _resizeStopTimer.Tick += (s, e) =>
            {
                _resizeStopTimer.Stop();
                _mtr.ResizeToCanvas(ModtermCanvas.ActualWidth, ModtermCanvas.ActualHeight);
            };

            this.InitializeFlyouts();

            // Update labels and ensure a draw pass runs so deferred ConPTY start can measure the canvas.
            _mtr.UpdateTitleBarLabels();
        }

        private void SaveConfig()
        {
            if (!_saveConfiguration)
            {
                return;
            }

            WriteConfigurationToDisk(_uac);
        }

        private void WriteConfigurationToDisk(UserAppConfiguration configuration)
        {
            string json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_userAppConfigPath, json);
        }

        private static string GetBundledDefaultThemesDirectory()
            => Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultThemes");

        private void CopyDefaultThemeFiles(bool overwriteExisting)
        {
            string sourceDirectory = GetBundledDefaultThemesDirectory();
            if (!Directory.Exists(sourceDirectory))
            {
                return;
            }

            foreach (string sourcePath in Directory.GetFiles(sourceDirectory, "theme_*.json"))
            {
                string destinationPath = Path.Combine(_userConfigDirectory, Path.GetFileName(sourcePath));
                if (!overwriteExisting && File.Exists(destinationPath))
                {
                    continue;
                }

                File.Copy(sourcePath, destinationPath, overwriteExisting);
            }
        }

        private ThemeConfiguration LoadThemeConfiguration(string themeName)
        {
            string fileName = $"theme_{themeName}.json";
            string userThemePath = Path.Combine(_userConfigDirectory, fileName);
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

            return theme;
        }

        private void LoadThemeNames()
        {
            var themeFiles = Directory.GetFiles(_userConfigDirectory, "theme_*.json");
            _themeNames = themeFiles
                .Select(f => Path.GetFileNameWithoutExtension(f).Substring(6))
                .ToList();
        }

        private bool TryLoadUserConfiguration(out UserAppConfiguration configuration)
        {
            try
            {
                string json = File.ReadAllText(_userAppConfigPath);
                var loadedConfiguration = JsonSerializer.Deserialize<UserAppConfiguration>(json);
                configuration = ValidateLoadedConfiguration(loadedConfiguration);
                return true;
            }
            catch
            {
                configuration = GetDefaultAppConfiguration();
                return false;
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

        private void SetUserConfiguration(UserAppConfiguration configuration)
        {
            if (_uac is not null)
            {
                _uac.PropertyChanged -= UserConfiguration_PropertyChanged;
                _uac.ThemeConfiguration.PropertyChanged -= ThemeConfiguration_PropertyChanged;
            }

            _uac = configuration;
            NormalizeShellConfigurations(_uac);
            _uac.PropertyChanged += UserConfiguration_PropertyChanged;
            _uac.ThemeConfiguration.PropertyChanged += ThemeConfiguration_PropertyChanged;
        }

        // Migrates legacy "conhost --headless ... -- <shell>" entries to direct shell launches.
        // The nested conhost was a fixed-size console host that prevented live resize; attaching
        // the shell directly to our ConPTY lets ResizePseudoConsole reach it.
        private static void NormalizeShellConfigurations(UserAppConfiguration configuration)
        {
            if (configuration is null)
                return;

            if (configuration.TerminalShell is not null)
                NormalizeShell(configuration.TerminalShell);

            if (configuration.ShellConfigurations is not null)
            {
                foreach (var shell in configuration.ShellConfigurations)
                    NormalizeShell(shell);
            }
        }

        private static void NormalizeShell(Shell shell)
        {
            if (shell is null || !string.Equals(shell.Path, "conhost", StringComparison.OrdinalIgnoreCase))
                return;

            string args = shell.Arguments ?? string.Empty;
            int sep = args.IndexOf(" -- ", StringComparison.Ordinal);
            if (sep < 0)
                return;

            string remainder = args.Substring(sep + 4).Trim();
            if (string.IsNullOrEmpty(remainder))
                return;

            string targetPath;
            string targetArgs;
            if (remainder.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = remainder.IndexOf('"', 1);
                targetPath = end > 0 ? remainder.Substring(1, end - 1) : remainder.Trim('"');
                targetArgs = end > 0 ? remainder.Substring(end + 1).Trim() : string.Empty;
            }
            else
            {
                int space = remainder.IndexOf(' ');
                targetPath = space > 0 ? remainder.Substring(0, space) : remainder;
                targetArgs = space > 0 ? remainder.Substring(space + 1).Trim() : string.Empty;
            }

            shell.Path = targetPath;
            shell.Arguments = targetArgs;
        }

        private void SetThemeConfiguration(ThemeConfiguration themeConfig)
        {
            _uac.ThemeConfiguration.PropertyChanged -= ThemeConfiguration_PropertyChanged;
            _uac.ThemeConfiguration = themeConfig;
            _uac.ThemeConfiguration.PropertyChanged += ThemeConfiguration_PropertyChanged;
        }

        private void ApplyCurrentUserConfiguration(bool applyWindowBounds)
        {
            if (applyWindowBounds)
            {
                var position = _uac.LastWindowLocation;
                var size = _uac.WindowSize;
                var rectInt32 = WindowPlacement.EnsureVisible(new RectInt32
                {
                    X = (int)position.X,
                    Y = (int)position.Y,
                    Width = (int)size.Width,
                    Height = (int)size.Height
                });

                if (rectInt32.X != (int)position.X || rectInt32.Y != (int)position.Y)
                {
                    _uac.LastWindowLocation = new Point(rectInt32.X, rectInt32.Y);
                }

                this.AppWindow.MoveAndResize(rectInt32);
            }

            _mtr.CurrentFont = _uac.TerminalFont;
            _mtr.CurrentControlFont = _uac.LabelFont;
            _mtr.CurrentFontSize = _uac.TerminalFontSize;
            _mtr.CurrentCursorStyle = _uac.TerminalCursor;
            _mtr.SetColorConfiguration(_uac.ThemeConfiguration, this);
            RootGrid.Background = _mtr.GetBackgroundBrush();
        }

        private void ResetDefaultConfiguration()
        {
            _saveConfiguration = true;
            CopyDefaultThemeFiles(overwriteExisting: true);
            SetUserConfiguration(GetDefaultAppConfiguration());
            ApplyCurrentUserConfiguration(applyWindowBounds: false);
            _mtr.InitializeDisplayLabels();
            RestartTerminalForLayoutChange();
            LoadThemeNames();
            InitializeFlyouts();
            _mtr.UpdateTitleBarLabels();
        }

        private void RequestConfigurationReload()
        {
            _configurationReloadTimer ??= DispatcherQueue.CreateTimer();
            _configurationReloadTimer.Interval = TimeSpan.FromMilliseconds(150);
            _configurationReloadTimer.Tick -= ConfigurationReloadTimer_Tick;
            _configurationReloadTimer.Tick += ConfigurationReloadTimer_Tick;
            _configurationReloadTimer.Stop();
            _configurationReloadTimer.Start();
        }

        private async void ConfigurationReloadTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            await ReloadConfigurationFromDiskAsync();
        }

        private async Task ReloadConfigurationFromDiskAsync()
        {
            if (!File.Exists(_userAppConfigPath))
            {
                return;
            }

            var previousConfiguration = _uac;
            var currentWindowSize = new Size(this.AppWindow.Size.Width, this.AppWindow.Size.Height);
            var currentWindowLocation = new Point(this.AppWindow.Position.X, this.AppWindow.Position.Y);
            if (TryLoadUserConfiguration(out var loadedConfiguration))
            {
                _saveConfiguration = true;
                SetUserConfiguration(loadedConfiguration);
                _uac.WindowSize = currentWindowSize;
                _uac.LastWindowLocation = currentWindowLocation;
                ApplyCurrentUserConfiguration(applyWindowBounds: false);
                _mtr.InitializeDisplayLabels();
                LoadThemeNames();
                InitializeFlyouts();
                _mtr.UpdateTitleBarLabels();

                if (HasTerminalShellChanged(previousConfiguration, loadedConfiguration))
                {
                    RestartTerminalForLayoutChange();
                }

                return;
            }

            _saveConfiguration = false;
            SetUserConfiguration(GetDefaultAppConfiguration());
            _uac.WindowSize = currentWindowSize;
            _uac.LastWindowLocation = currentWindowLocation;
            ApplyCurrentUserConfiguration(applyWindowBounds: false);
            _mtr.InitializeDisplayLabels();
            LoadThemeNames();
            InitializeFlyouts();
            _mtr.UpdateTitleBarLabels();

            if (HasTerminalShellChanged(previousConfiguration, _uac))
            {
                RestartTerminalForLayoutChange();
            }

            await ShowConfigurationLoadFailureDialogAsync();
        }

        private static bool HasTerminalShellChanged(UserAppConfiguration previousConfiguration, UserAppConfiguration nextConfiguration)
        {
            return previousConfiguration.TerminalShell.Name != nextConfiguration.TerminalShell.Name ||
                previousConfiguration.TerminalShell.Path != nextConfiguration.TerminalShell.Path ||
                previousConfiguration.TerminalShell.Arguments != nextConfiguration.TerminalShell.Arguments;
        }

        private void UserConfiguration_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            _mtr.UpdateTitleBarLabels();
        }

        private void ThemeConfiguration_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            _mtr.UpdateTitleBarLabels();
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_showConfigLoadFailureDialog)
            {
                return;
            }

            _showConfigLoadFailureDialog = false;
            await ShowConfigurationLoadFailureDialogAsync();
        }

        private async Task ShowConfigurationLoadFailureDialogAsync()
        {
            if (RootGrid.XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Configuration Load Failed",
                Content = "Your configration failed to load. Using the default, any visual changes won't be saved.",
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
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

        private void OpenConfigurationInNotepad()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{_userAppConfigPath}\"",
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }

        private void LaunchThemeEditor()
        {
            // Persist current configuration (including active theme + palette) so modtermTE loads the user's current state.
            SaveConfig();

            string baseDirectory = AppContext.BaseDirectory;
            // Prefer ThemeEditor\ (avoids App.xbf/.pri collisions with modterm), fall back to
            // same-folder layout used by older installs / debug copies.
            string themeEditorDirectory = Path.Combine(baseDirectory, "ThemeEditor");
            if (!Directory.Exists(themeEditorDirectory))
            {
                themeEditorDirectory = baseDirectory;
            }

            string themeEditorPath = Path.Combine(themeEditorDirectory, "modtermTE.exe");
            string themeEditorDllPath = Path.Combine(themeEditorDirectory, "modtermTE.dll");

            if (!File.Exists(themeEditorPath))
            {
                _ = ShowSimpleDialogAsync(
                    "Theme Editor Not Found",
                    $"Could not find modtermTE.exe at:\n{themeEditorPath}");
                return;
            }

            if (!File.Exists(themeEditorDllPath))
            {
                _ = ShowSimpleDialogAsync(
                    "Theme Editor Not Found",
                    $"modtermTE.exe is present but modtermTE.dll is missing at:\n{themeEditorDllPath}");
                return;
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = themeEditorPath,
                    WorkingDirectory = themeEditorDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _ = ShowSimpleDialogAsync("Theme Editor Launch Failed", ex.Message);
            }
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            PersistWindowSize();

            // Coalesce rapid size events during a drag; the live resize runs when they pause.
            _resizeStopTimer.Stop();
            _resizeStopTimer.Start();
        }

        private void PersistWindowSize()
        {
            _uac.WindowSize = new Size(
                this.AppWindow.Size.Width,
                this.AppWindow.Size.Height);
        }

        private void PersistLastWindowLocation()
        {
            if (!_saveConfiguration)
            {
                return;
            }

            _uac.LastWindowLocation = new Point(
                this.AppWindow.Position.X,
                this.AppWindow.Position.Y);
        }



        private void RestartTerminalForLayoutChange()
        {
            ReplaceTerminalInstance(startImmediately: false);
        }

        private ConPTYTerminal CreateTerminalInstance()
        {
            var terminal = new ConPTYTerminal();
            terminal.OutputReceived += _mtr.OnOutputReceived;
            terminal.TerminalExited += OnTerminalExited;
            return terminal;
        }

        private void DisposeTerminalInstance(ConPTYTerminal? terminal)
        {
            if (terminal is null)
                return;

            terminal.OutputReceived -= _mtr.OnOutputReceived;
            terminal.TerminalExited -= OnTerminalExited;
            terminal.Dispose();
        }

        private void ReplaceTerminalInstance(bool startImmediately, Shell? shellOverride = null)
        {
            _terminalRestartInProgress = true;
            try
            {
                var nextShell = shellOverride ?? _uac.TerminalShell;
                var previousTerminal = ConPtyTerminal;
                ConPtyTerminal = CreateTerminalInstance();
                DisposeTerminalInstance(previousTerminal);

                if (startImmediately)
                {
                    ConPtyTerminal.Start(nextShell, _mtr.Lines, _mtr.Columns);
                    _mtr.Terminal.Resize(_mtr.Columns, _mtr.Lines);
                    ConPtyTerminal.Resize((short)_mtr.Columns, (short)_mtr.Lines);
                }
                else
                {
                    // terminal start is deferred until the first draw pass so we can measure
                    ModtermCanvas.Invalidate();
                }
            }
            finally
            {
                _terminalRestartInProgress = false;
            }
        }

        private Task SwitchTerminalShellAsync(Shell shell)
        {
            ReplaceTerminalInstance(startImmediately: true, shellOverride: shell);
            return Task.CompletedTask;
        }

        private void OnTerminalExited(object? sender, EventArgs e)
        {
            if (_terminalRestartInProgress || !ReferenceEquals(sender, ConPtyTerminal))
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_terminalRestartInProgress || !ReferenceEquals(sender, ConPtyTerminal))
                    return;

                Close();
            });
        }



        private void InitializeFlyouts()
        {
            _flyout = new MenuFlyout();

            var copyItem = new MenuFlyoutItem { Text = "Copy" };
            copyItem.Click += (_, __) =>
            {
                _mtr.CopySelectedTextToClipboard();
            };
            _flyout.Items.Add(copyItem);

            var pasteItem = new MenuFlyoutItem { Text = "Paste" };
            pasteItem.Click += (_, __) => _mtr.PasteFromClipboard();
            _flyout.Items.Add(pasteItem);

            _flyout.Items.Add(new MenuFlyoutSeparator());

            var windowOpacityItem = new MenuFlyoutSubItem { Text = "Window Opacity" };
            for (int i = 0; i <= 10; i++)
            {
                int pct = i * 10;
                string label = pct switch
                {
                    0 => "0 (transparent)",
                    100 => "100 (opaque)",
                    _ => pct.ToString()
                };
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (_, __) =>
                {
                    _mtr.OpacityPct = pct;
                    _uac.ThemeConfiguration.WindowOpacityPct = pct;
                    _mtr.UpdateTitleBarLabels();
                };
                windowOpacityItem.Items.Add(item);
            }
            _flyout.Items.Add(windowOpacityItem);

            // theme
            var themeItem = new MenuFlyoutSubItem { Text = "Theme" };
            foreach (var preset in _themeNames)
            {
                var item = new MenuFlyoutItem { Text = preset };
                item.Click += (_, __) =>
                {
                    // deserialize the theme config from disk and apply it
                    string themePath = Path.Combine(_userConfigDirectory, $"theme_{preset}.json");
                    ThemeConfiguration themeConfig = JsonSerializer.Deserialize<ThemeConfiguration>(File.ReadAllText(themePath)) ?? _uac.ThemeConfiguration;
                    _mtr.SetColorConfiguration(themeConfig, this);
                    SetThemeConfiguration(themeConfig);
                };
                themeItem.Items.Add(item);
            }
            _flyout.Items.Add(themeItem);

            var launchThemeEditorItem = new MenuFlyoutItem { Text = "Launch Configuration and Theme Editor" };
            launchThemeEditorItem.Click += (_, __) => LaunchThemeEditor();
            _flyout.Items.Add(launchThemeEditorItem);

            var resetDefaultsItem = new MenuFlyoutItem { Text = "Reset Default Configuration Files" };
            resetDefaultsItem.Click += (_, __) => ResetDefaultConfiguration();
            _flyout.Items.Add(resetDefaultsItem);

            var editConfigItem = new MenuFlyoutItem { Text = "Edit Configuration File" };
            editConfigItem.Click += (_, __) => OpenConfigurationInNotepad();
            _flyout.Items.Add(editConfigItem);

            var reloadConfigItem = new MenuFlyoutItem { Text = "Reload Configuration File" };
            reloadConfigItem.Click += async (_, __) => await ReloadConfigurationFromDiskAsync();
            _flyout.Items.Add(reloadConfigItem);
        }
    }
}
