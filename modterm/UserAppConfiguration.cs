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
        public Rect WindowLocation { get; set; }
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
        public string ColorConfiguration { get => _colorConfiguration; set { _colorConfiguration = value; OnPropertyChanged(nameof(ColorConfiguration)); } }
        private string _colorConfiguration = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
