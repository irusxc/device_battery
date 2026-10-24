using System;
using System.Windows;
using System.Windows.Media;

namespace PeripheralPower
{
    internal sealed class DeviceArt : FrameworkElement
    {
        private readonly string kind;
        internal DeviceArt(string value) { kind = value; Width = 106; Height = 100; IsHitTestVisible = false; }
        private static SolidColorBrush B(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        private static Pen P(string hex, double size) { return new Pen(B(hex), size); }
        protected override void OnRender(DrawingContext d)
        {
            base.OnRender(d);
            d.DrawRoundedRectangle(B("#F1F3ED"), null, new Rect(0, 0, 106, 100), 13, 13);
            if (kind == "mouse") {
                d.PushTransform(new RotateTransform(-18, 53, 48));
                d.DrawEllipse(B("#DCE1D5"), null, new Point(55, 82), 25, 7);
                var body = new LinearGradientBrush(B("#FFFFFF").Color, B("#D9DED4").Color, 38);
                d.DrawRoundedRectangle(body, P("#C8CFC3", 1), new Rect(29, 12, 47, 73), 23, 26);
                d.DrawLine(P("#C7CEC2", 1), new Point(52.5, 13), new Point(52.5, 40));
                d.DrawGeometry(null, P("#CED5C8", 1), Geometry.Parse("M 30,40 Q 52,47 75,40"));
                d.DrawRoundedRectangle(B("#516052"), null, new Rect(49, 24, 7, 14), 3, 3);
                d.DrawLine(P("#A8B79C", 1), new Point(52, 27), new Point(52, 35));
                d.DrawEllipse(B("#7C9863"), null, new Point(53, 63), 2, 2);
                d.Pop();
            } else if (kind == "keyboard") {
                d.PushTransform(new RotateTransform(-9, 53, 50));
                d.DrawRoundedRectangle(B("#D6DDCF"), null, new Rect(7, 32, 93, 45), 6, 6);
                d.DrawRoundedRectangle(B("#E4E9DE"), P("#C6CEBF", 1), new Rect(6, 27, 93, 45), 5, 5);
                for (int row = 0; row < 4; row++) for (int col = 0; col < 12; col++) {
                    if (row == 3 && col > 2 && col < 8) continue;
                    d.DrawRoundedRectangle(B(row == 0 && col == 0 ? "#849B70" : col > 9 ? "#C3CEB9" : "#FAFCF6"), null, new Rect(10 + col * 7, 31 + row * 9, 5.5, 6.5), 1.4, 1.4);
                }
                d.DrawRoundedRectangle(B("#FAFCF6"), null, new Rect(31, 58, 40, 6.5), 1.4, 1.4);
                d.Pop();
            } else if (kind == "headset") {
                d.DrawEllipse(B("#DCE1D5"), null, new Point(54, 83), 29, 5);
                d.DrawGeometry(null, P("#405144", 9), Geometry.Parse("M 25,62 L 25,43 C 25,7 81,7 81,43 L 81,62"));
                d.DrawGeometry(null, P("#9CAD8E", 4), Geometry.Parse("M 27,41 C 27,11 79,11 79,41"));
                d.DrawRoundedRectangle(B("#718668"), null, new Rect(18, 46, 19, 31), 8, 8);
                d.DrawRoundedRectangle(B("#718668"), null, new Rect(69, 46, 19, 31), 8, 8);
                d.DrawRoundedRectangle(B("#334637"), null, new Rect(30, 49, 9, 28), 4, 4);
                d.DrawRoundedRectangle(B("#334637"), null, new Rect(67, 49, 9, 28), 4, 4);
                d.DrawGeometry(null, P("#405144", 3), Geometry.Parse("M 78,72 Q 72,88 57,83"));
            } else {
                d.PushTransform(new RotateTransform(-17, 53, 51));
                d.DrawRoundedRectangle(B("#BDCCB0"), P("#ACBDA0", 1), new Rect(38, 19, 29, 32), 3, 3);
                d.DrawRectangle(B("#6B7D61"), null, new Rect(43, 28, 6, 7));
                d.DrawRectangle(B("#6B7D61"), null, new Rect(56, 28, 6, 7));
                d.DrawRoundedRectangle(B("#3B5442"), null, new Rect(32, 46, 41, 33), 8, 8);
                d.DrawEllipse(B("#C6DCA6"), null, new Point(53, 62), 3, 3);
                d.Pop();
            }
        }
    }
}
