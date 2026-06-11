using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace modtermTE
{
    public sealed partial class ModtermTeWindow : Window
    {
        public ModtermTeWindow()
        {
            InitializeComponent();

            SystemBackdrop = new DesktopAcrylicBackdrop();

            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            SetTitleBar(AppTitleBar);
        }
    }
}
