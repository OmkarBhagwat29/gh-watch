using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Gh.Watch.Canvas
{
    // A frame-ring overlay that draws the GH capsule border on top of all WebView2 panels.
    //
    // Win32 Region = donut (full bounds minus inner body rect).
    //   - DWM composites body-area pixels from the lower-z WebView2 panel (it doesn't own them).
    //   - GDI+ OnPaint is auto-clipped to the Region so the body fill from GH_Capsule never appears.
    //
    // WS_EX_TRANSPARENT → WM_NCHITTEST returns HTTRANSPARENT for every pixel in the Region,
    //   so all mouse events (clicks, scroll-wheel, drag) fall through to the GH canvas beneath.
    //
    // NO ControlStyles.OptimizedDoubleBuffer — double-buffered blits write the entire control
    //   bounds to screen including the body area, which would overwrite the WebView2 content.
    //   Direct-to-screen GDI+ respects the Region and leaves body pixels untouched.
    internal sealed class WatchFrameOverlay : Control
    {
        private string _nickName  = string.Empty;
        private bool   _selected, _locked, _hidden;
        private int    _headerPx  = 24;
        private int    _borderPx  = 5;

        public WatchFrameOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        // Called every Sync() to reposition the overlay and rebuild the donut Region.
        // headerPx / borderPx are the header height and side-inset in screen pixels,
        // derived from projecting the document-space Bounds and BodyRect.
        public void Reposition(Point location, Size size, int headerPx, int borderPx)
        {
            _headerPx = headerPx;
            _borderPx = borderPx;
            Location  = location;
            Size      = size;

            // Build the donut: outer rect minus inner body rect.
            // FillMode.Alternate makes the inner rect a hole in the region.
            int innerW = size.Width  - borderPx * 2;
            int innerH = size.Height - headerPx - borderPx;
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                path.AddRectangle(new Rectangle(0, 0, size.Width, size.Height));
                if (innerW > 0 && innerH > 0)
                    path.AddRectangle(new Rectangle(borderPx, headerPx, innerW, innerH));
                Region = new Region(path);
            }
        }

        public void UpdateState(string nickName, bool selected, bool locked, bool hidden)
        {
            bool dirty = _nickName != nickName || _selected != selected
                      || _locked   != locked   || _hidden   != hidden;
            _nickName = nickName;
            _selected = selected;
            _locked   = locked;
            _hidden   = hidden;
            if (dirty) Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { } // Region clips us; suppress default

        protected override void OnPaint(PaintEventArgs e)
        {
            // GH_Capsule is drawn over the full client rect.
            // The Region (donut) auto-clips GDI+ output, so the body fill is never painted.
            var bounds  = new RectangleF(0, 0, Width, Height);
            var capsule = GH_Capsule.CreateCapsule(bounds, GH_Palette.Normal);
            capsule.AddInputGrip(_headerPx * 0.5f);
            capsule.Render(e.Graphics, _selected, _locked, _hidden);
            capsule.RenderEngine.RenderGrips(e.Graphics);
            capsule.Dispose();

            var headerRect = new RectangleF(0, 0, Width, _headerPx);
            using (var brush = new SolidBrush(Color.FromArgb(50, 50, 50)))
            using (var sf    = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString(_nickName, SystemFonts.DefaultFont, brush, headerRect, sf);
        }
    }
}
