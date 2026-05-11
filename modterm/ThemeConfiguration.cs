using Windows.UI;
using System.ComponentModel;
using System.Reflection.Metadata.Ecma335;

namespace modterm
{
    public class ThemeConfiguration : INotifyPropertyChanged
    {
        public required string Name { get; set; }
        public Color OutputColor { get => _outputColor; set { _outputColor = value; OnPropertyChanged(nameof(OutputColor)); } }
        private Color _outputColor;
        public Color OutputBlurColor { get => _outputBlurColor; set { _outputBlurColor = value; OnPropertyChanged(nameof(OutputBlurColor)); } }
        private Color _outputBlurColor;
        public Color ControlColor { get => _controlColor; set { _controlColor = value; OnPropertyChanged(nameof(ControlColor)); } }
        private Color _controlColor;
        public Color ControlBlurColor { get => _controlBlurColor; set { _controlBlurColor = value; OnPropertyChanged(nameof(ControlBlurColor)); } }
        private Color _controlBlurColor;
        public float BlurAmount { get => _blurAmount; set { _blurAmount = value; OnPropertyChanged(nameof(BlurAmount)); } }
        private float _blurAmount;
        public int WindowOpacityPct { get => _windowOpacityPct; set { _windowOpacityPct = value; OnPropertyChanged(nameof(WindowOpacityPct)); } }
        private int _windowOpacityPct;
        public Color WindowColor { get => _windowColor; set { _windowColor = value; OnPropertyChanged(nameof(WindowColor)); } }
        private Color _windowColor;
        public BackdropKind BackdropKind { get => _backdropKind; set { _backdropKind = value; OnPropertyChanged(nameof(BackdropKind)); } }
        private BackdropKind _backdropKind;
        public Color ControlEngagedColor { get => _controlEngagedColor; set { _controlEngagedColor = value; OnPropertyChanged(nameof(ControlEngagedColor)); } }
        private Color _controlEngagedColor;
        public Color ControlEngagedHoverColor { get => _controlEngagedHoverColor; set { _controlEngagedHoverColor = value; OnPropertyChanged(nameof(ControlEngagedHoverColor)); } }
        private Color _controlEngagedHoverColor;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
