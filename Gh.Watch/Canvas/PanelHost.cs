using Gh.Watch.Dtos;
using Grasshopper.GUI.Canvas;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Gh.Watch.Canvas
{
    // Manages WatchPanel (WebView2 body) and WatchFrameOverlay (capsule frame ring)
    // lifetimes on the GH canvas.
    //
    // Z-order contract maintained every Sync():
    //   BringToFront(_panel)   — WebView2 for this component rises above all others
    //   BringToFront(_overlay) — Frame ring rises above even this component's WebView2
    //
    // GH renders back-to-front, so the last Sync() each frame belongs to the visually
    // topmost component, leaving its pair at the top of the Win32 z-order.
    // Final order (front → back): front_overlay, front_panel, back_overlay, back_panel.
    internal sealed class PanelHost
    {
        private WatchPanel        _panel;
        private WatchFrameOverlay _overlay;
        private List<SendDataDto> _lastData;

        public void Sync(GH_Canvas canvas, RectangleF fullBounds, RectangleF bodyRect,
                         string nickName, bool selected, bool locked, bool hidden)
        {
            EnsureControls(canvas);

            // ── WebView2 panel at body rect ───────────────────────────────────
            PointF btl = canvas.Viewport.ProjectPoint(new PointF(bodyRect.Left,  bodyRect.Top));
            PointF bbr = canvas.Viewport.ProjectPoint(new PointF(bodyRect.Right, bodyRect.Bottom));
            _panel.Location = new Point((int)btl.X, (int)btl.Y);
            _panel.Size     = new Size(Math.Max(1, (int)(bbr.X - btl.X)),
                                       Math.Max(1, (int)(bbr.Y - btl.Y)));
            _panel.Visible  = true;

            // ── Frame overlay at full component bounds ────────────────────────
            PointF ftl = canvas.Viewport.ProjectPoint(new PointF(fullBounds.Left,  fullBounds.Top));
            PointF fbr = canvas.Viewport.ProjectPoint(new PointF(fullBounds.Right, fullBounds.Bottom));
            int headerPx = Math.Max(1, (int)btl.Y - (int)ftl.Y); // body top − full top
            int borderPx = Math.Max(1, (int)btl.X - (int)ftl.X); // body left − full left
            _overlay.Reposition(new Point((int)ftl.X, (int)ftl.Y),
                                new Size(Math.Max(1, (int)(fbr.X - ftl.X)),
                                         Math.Max(1, (int)(fbr.Y - ftl.Y))),
                                headerPx, borderPx);
            _overlay.UpdateState(nickName, selected, locked, hidden);
            _overlay.Visible = true;

            // Maintain z-order: panel first, overlay on top.
            // Both are called unconditionally so GH's back-to-front render order
            // is reflected in Win32 z-order each frame.
            _panel.BringToFront();
            _overlay.BringToFront();
        }

        public void Store(List<SendDataDto> data)
        {
            _lastData = data;
            Flush();
        }

        public void Destroy(GH_Canvas canvas)
        {
            if (_panel != null && !_panel.IsDisposed)
            {
                canvas?.Controls.Remove(_panel);
                _panel.WebViewReady -= OnWebViewReady;
                _panel.Dispose();
                _panel = null;
            }

            if (_overlay != null && !_overlay.IsDisposed)
            {
                canvas?.Controls.Remove(_overlay);
                _overlay.Dispose();
                _overlay = null;
            }
        }

        private void EnsureControls(GH_Canvas canvas)
        {
            if (_panel == null || _panel.IsDisposed)
            {
                _panel = new WatchPanel();
                _panel.WebViewReady += OnWebViewReady;
                canvas.Controls.Add(_panel);
                canvas.DocumentChanged += OnDocumentChanged;
            }

            if (_overlay == null || _overlay.IsDisposed)
            {
                _overlay = new WatchFrameOverlay();
                canvas.Controls.Add(_overlay);
            }
        }

        private void OnDocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e)
        {
            sender.DocumentChanged -= OnDocumentChanged;
            Destroy(sender);
        }

        private void OnWebViewReady(object sender, EventArgs e) => Flush();

        private void Flush()
        {
            if (_lastData == null || _panel == null || _panel.IsDisposed || !_panel.IsReady) return;
            foreach (var dto in _lastData)
                _panel.SendGeometry(dto);
        }
    }
}
