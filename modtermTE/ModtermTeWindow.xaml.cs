using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;

namespace modtermTE
{
    public sealed partial class ModtermTeWindow : Window
    {
        private LiveConfigurationPublisher? _liveConfigurationPublisher;

        public ModtermTeWindow()
        {
            InitializeComponent();

            SystemBackdrop = new DesktopAcrylicBackdrop();

            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            SetTitleBar(AppTitleBar);

            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 925));

            _liveConfigurationPublisher = new LiveConfigurationPublisher(DispatcherQueue, _configurationStore);
            InitializeSettings();

            UpdateThemeHeader();
        }
    }
}
