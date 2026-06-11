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
        public Color LabelColor { get => _labelColor; set { _labelColor = value; OnPropertyChanged(nameof(LabelColor)); } }
        private Color _labelColor;
        public Color LabelBlurColor { get => _labelBlueColor; set { _labelBlueColor = value; OnPropertyChanged(nameof(LabelBlurColor)); } }
        private Color _labelBlueColor;
        public float BlurAmount { get => _blurAmount; set { _blurAmount = value; OnPropertyChanged(nameof(BlurAmount)); } }
        private float _blurAmount;
        public int WindowOpacityPct { get => _windowOpacityPct; set { _windowOpacityPct = value; OnPropertyChanged(nameof(WindowOpacityPct)); } }
        private int _windowOpacityPct;
        public Color WindowColor { get => _windowColor; set { _windowColor = value; OnPropertyChanged(nameof(WindowColor)); } }
        private Color _windowColor;
        public BackdropKind BackdropKind { get => _backdropKind; set { _backdropKind = value; OnPropertyChanged(nameof(BackdropKind)); } }
        private BackdropKind _backdropKind;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
