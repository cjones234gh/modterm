using Windows.UI.Composition;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinUIEx;

namespace modterm
{
    public partial class BlurredBackdrop : CompositionBrushBackdrop
    {
        protected override CompositionBrush CreateBrush(Compositor compositor)
            => compositor.CreateHostBackdropBrush();
    }
}
