using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace modterm
{
    internal class DrawTextCall
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public string Text;
        public Color Color;
        public Color BackgroundColor;
        public CanvasTextFormat TextFormat;

        public DrawTextCall() { }

        public DrawTextCall(string text, float x, float y, float width, Color color, Color backgroundColor, CanvasTextFormat textFormat)
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = textFormat.FontSize * 1.1f;    // this can be overriden after construction if needed, but this is a good default height based on the font size.
            Color = color;
            BackgroundColor = backgroundColor;
            TextFormat = textFormat;
        }
    }
}
