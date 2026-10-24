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
            if (kind == "mouse") {
                d.PushTransform(new RotateTransform(-18, 53, 48));
                d.DrawEllipse(B("#101010"), null, new Point(55, 82), 25, 7);
                var body = new LinearGradientBrush(B("#E6E6E6").Color, B("#969696").Color, 38);
                d.DrawRoundedRectangle(body, P("#B0B0B0", 1), new Rect(29, 12, 47, 73), 23, 26);
                d.DrawLine(P("#8A8A8A", 1), new Point(52.5, 13), new Point(52.5, 40));
                d.DrawGeometry(null, P("#9D9D9D", 1), Geometry.Parse("M 30,40 Q 52,47 75,40"));
                d.DrawRoundedRectangle(B("#454545"), null, new Rect(49, 24, 7, 14), 3, 3);
                d.DrawLine(P("#999999", 1), new Point(52, 27), new Point(52, 35));
                d.DrawEllipse(B("#727272"), null, new Point(53, 63), 2, 2);
                d.Pop();
            } else if (kind == "keyboard") {
                d.PushTransform(new RotateTransform(-9, 53, 50));
                d.DrawRoundedRectangle(B("#333333"), null, new Rect(7, 32, 93, 45), 6, 6);
                d.DrawRoundedRectangle(B("#4A4A4A"), P("#666666", 1), new Rect(6, 27, 93, 45), 5, 5);
                for (int row = 0; row < 4; row++) for (int col = 0; col < 12; col++) {
                    if (row == 3 && col > 2 && col < 8) continue;
                    d.DrawRoundedRectangle(B(row == 0 && col == 0 ? "#BBBBBB" : col > 9 ? "#707070" : "#999999"), null, new Rect(10 + col * 7, 31 + row * 9, 5.5, 6.5), 1.4, 1.4);
                }
                d.DrawRoundedRectangle(B("#999999"), null, new Rect(31, 58, 40, 6.5), 1.4, 1.4);
                d.Pop();
            } else if (kind == "headset") {
                d.DrawEllipse(B("#101010"), null, new Point(54, 83), 29, 5);
                d.DrawGeometry(null, P("#737373", 9), Geometry.Parse("M 25,62 L 25,43 C 25,7 81,7 81,43 L 81,62"));
                d.DrawGeometry(null, P("#BBBBBB", 4), Geometry.Parse("M 27,41 C 27,11 79,11 79,41"));
                d.DrawRoundedRectangle(B("#777777"), null, new Rect(18, 46, 19, 31), 8, 8);
                d.DrawRoundedRectangle(B("#777777"), null, new Rect(69, 46, 19, 31), 8, 8);
                d.DrawRoundedRectangle(B("#333333"), null, new Rect(30, 49, 9, 28), 4, 4);
                d.DrawRoundedRectangle(B("#333333"), null, new Rect(67, 49, 9, 28), 4, 4);
                d.DrawGeometry(null, P("#737373", 3), Geometry.Parse("M 78,72 Q 72,88 57,83"));
            } else if (kind == "printer" || kind == "scanner") {
                d.DrawRoundedRectangle(B("#303030"), null, new Rect(15, 35, 76, 40), 7, 7);
                d.DrawRoundedRectangle(B("#777777"), null, new Rect(20, 25, 66, 24), 4, 4);
                d.DrawRectangle(B("#CCCCCC"), null, new Rect(29, 13, 48, 27));
                d.DrawRoundedRectangle(B("#191919"), null, new Rect(29, 57, 48, 9), 2, 2);
                d.DrawRectangle(B("#A8A8A8"), null, new Rect(31, 62, 44, 25));
                d.DrawLine(P("#777777", 1), new Point(38, 71), new Point(67, 71));
                d.DrawLine(P("#777777", 1), new Point(38, 77), new Point(61, 77));
                d.DrawEllipse(B("#BBBBBB"), null, new Point(80, 52), 2, 2);
            } else if (kind == "camera") {
                d.DrawRoundedRectangle(B("#777777"), null, new Rect(14, 26, 78, 40), 12, 12);
                d.DrawEllipse(B("#292929"), P("#AAAAAA", 2), new Point(53, 46), 15, 15);
                d.DrawEllipse(B("#454545"), null, new Point(53, 46), 8, 8);
                d.DrawRoundedRectangle(B("#505050"), null, new Rect(46, 66, 14, 13), 2, 2);
                d.DrawRoundedRectangle(B("#666666"), null, new Rect(30, 78, 46, 5), 2, 2);
            } else {
                d.PushTransform(new RotateTransform(-17, 53, 51));
                d.DrawRoundedRectangle(B("#BBBBBB"), P("#AAAAAA", 1), new Rect(38, 19, 29, 32), 3, 3);
                d.DrawRectangle(B("#555555"), null, new Rect(43, 28, 6, 7));
                d.DrawRectangle(B("#555555"), null, new Rect(56, 28, 6, 7));
                d.DrawRoundedRectangle(B("#535353"), null, new Rect(32, 46, 41, 33), 8, 8);
                d.DrawEllipse(B("#C0C0C0"), null, new Point(53, 62), 3, 3);
                d.Pop();
            }
        }
    }
}
