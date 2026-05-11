using Windows.Foundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace modterm
{
    public class UserAppConfiguration : INotifyPropertyChanged
    {
        public Rect WindowLocation { get => _windowLocation; set { _windowLocation = value; OnPropertyChanged(nameof(WindowLocation)); } }
        private Rect _windowLocation;
        public string TerminalFont { get => _terminalFont; set { _terminalFont = value; OnPropertyChanged(nameof(TerminalFont)); } }
        private string _terminalFont = string.Empty;
        public string TerminalControlFont { get => _terminalControlFont; set { _terminalControlFont = value; OnPropertyChanged(nameof(TerminalControlFont)); } }
        private string _terminalControlFont = string.Empty;
        public float TerminalFontSize { get => _terminalFontSize; set { _terminalFontSize = value; OnPropertyChanged(nameof(TerminalFontSize)); } }
        private float _terminalFontSize;
        public Shell TerminalShell { get => _terminalShell; set { _terminalShell = value; OnPropertyChanged(nameof(TerminalShell)); } }
        private Shell _terminalShell = null!;
        public string TerminalCursor { get => _terminalCursor; set { _terminalCursor = value; OnPropertyChanged(nameof(TerminalCursor)); } }
        private string _terminalCursor = string.Empty;
        public ThemeConfiguration ThemeConfiguration { get => _themeConfiguration; set { _themeConfiguration = value; OnPropertyChanged(nameof(ThemeConfiguration)); } }
        private ThemeConfiguration _themeConfiguration = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
