using System;
using System.Drawing;

namespace client.Classes
{
    // Pure geometry in the same coordinate space as WinForms Screen and Form bounds.
    internal static class PopupPlacement
    {
        public static Point GetLocation(Rectangle Bounds, Rectangle WorkingArea, Point Anchor, Size Popup)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0 || Popup.Width <= 0 || Popup.Height <= 0)
                throw new ArgumentException("Popup and monitor dimensions must be positive.");
            Rectangle Work = Rectangle.Intersect(Bounds, WorkingArea);
            if (Work.Width <= 0 || Work.Height <= 0) Work = Bounds;
            long X = (long)Anchor.X - Popup.Width / 2;
            long Y = (long)Anchor.Y - Popup.Height - 20;
            if (Anchor.Y < Work.Top && Work.Top > Bounds.Top)
                Y = (long)Work.Top + 10;
            else if (Anchor.Y >= Work.Bottom && Work.Bottom < Bounds.Bottom)
                Y = (long)Work.Bottom - Popup.Height - 10;
            else if (Anchor.X < Work.Left && Work.Left > Bounds.Left)
            {
                X = (long)Work.Left + 10;
                Y = (long)Anchor.Y - Popup.Height / 2;
            }
            else if (Anchor.X >= Work.Right && Work.Right < Bounds.Right)
            {
                X = (long)Work.Right - Popup.Width - 10;
                Y = (long)Anchor.Y - Popup.Height / 2;
            }
            else if (Y < Work.Top && (long)Anchor.Y + 20 + Popup.Height <= Work.Bottom)
                Y = (long)Anchor.Y + 20;
            return new Point(Clamp(X, Work.Left, Work.Width, Popup.Width),
                Clamp(Y, Work.Top, Work.Height, Popup.Height));
        }

        private static int Clamp(long Desired, int Start, int Length, int Extent)
        {
            // An oversized popup cannot fit: retain its leading edge on the chosen display.
            if (Extent >= Length) return Start;
            int Inset = Math.Min(10, (Length - Extent) / 2);
            long Minimum = (long)Start + Inset;
            long Maximum = (long)Start + Length - Extent - Inset;
            return (int)Math.Max(Minimum, Math.Min(Maximum, Desired));
        }
    }
}
