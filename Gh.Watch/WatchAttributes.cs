using Gh.Watch.Canvas;
using Gh.Watch.Serialization;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gh.Watch
{
    // Controls how the Watch component looks and behaves on the Grasshopper canvas.
    // Responsibilities: layout, rendering, wire connectivity, size persistence.
    // Panel lifecycle and geometry buffering are delegated to PanelHost.
    public class WatchAttributes : GH_ResizableAttributes<Watch_Component>
    {
        private const float DefaultWidth  = 400f;
        private const float DefaultHeight = 324f; // 300 body + 24 header
        private const float HeaderHeight  = 24f;
        private const float BorderInset   = 5f;

        private readonly PanelHost _panelHost = new PanelHost();

        public WatchAttributes(Watch_Component owner) : base(owner)
            => Bounds = new RectangleF(0, 0, DefaultWidth, DefaultHeight);

        // ── GH_ResizableAttributes required overrides ─────────────────────────
        protected override Size MinimumSize  => new Size(120, 80);
        protected override Size MaximumSize  => new Size(4000, 4000);
        protected override Padding SizingBorders => new Padding(10);

        // ── Layout ────────────────────────────────────────────────────────────
        // Pin Bounds to the current Pivot and position the input param's wire grip.
        protected override void Layout()
        {
            SizeF size = Bounds.Size;
            if (size.IsEmpty) size = new SizeF(DefaultWidth, DefaultHeight);

            Bounds = new RectangleF(Pivot.X, Pivot.Y, size.Width, size.Height);

            if (Owner.Params.Input.Count > 0)
            {
                var p = Owner.Params.Input[0];
                float y = Bounds.Y + HeaderHeight * 0.5f;

                // GH_Capsule draws the grip nub ~23 canvas units inside the left border.
                // Pivot must match that centre exactly so connecting wires terminate correctly.
                const float GripOffset = 23f;

                p.Attributes.Pivot  = new PointF(Bounds.Left + GripOffset, y);
                p.Attributes.Bounds = new RectangleF(Bounds.Left - 20f + GripOffset, Bounds.Y, 20f, HeaderHeight);
            }
        }

        // ── Wire connectivity ─────────────────────────────────────────────────
        // GH_ResizableAttributes does not walk input params automatically;
        // we add them so GH can find the input grip for wire routing and hit-testing.
        public override void AppendToAttributeTree(List<IGH_Attributes> attributes)
        {
            attributes.Add(this);
            foreach (var p in Owner.Params.Input)
                if (p.Attributes != null)
                    attributes.Add(p.Attributes);
        }

        // ── Rendering ─────────────────────────────────────────────────────────
        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Wires)
            {
                foreach (IGH_Param p in Owner.Params.Input)
                    p.Attributes.RenderToCanvas(canvas, channel);
                return;
            }

            if (channel != GH_CanvasChannel.Objects) return;

            // Capsule — body must render before grip nubs or nubs get painted over.
            GH_Capsule capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Normal);
            capsule.AddInputGrip(Bounds.Y + HeaderHeight * 0.5f);
            capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            capsule.RenderEngine.RenderGrips(graphics);
            capsule.Dispose();

            // White body fill so the WebView blends in without a grey capsule showing through.
            using (var b = new SolidBrush(Color.White))
                graphics.FillRectangle(b, BodyRect);

            // NickName centred in the header strip.
            var headerRect = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, HeaderHeight);
            using (var brush = new SolidBrush(Color.FromArgb(50, 50, 50)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString(Owner.NickName, SystemFonts.DefaultFont, brush, headerRect, sf);

            // Sync both the WebView2 panel (body) and the frame overlay (full bounds).
            _panelHost.Sync(canvas, Bounds, BodyRect, Owner.NickName, Selected, Owner.Locked, Owner.Hidden);
        }

        // Body area: everything below the header, inset so the capsule border stays visible.
        private RectangleF BodyRect => new RectangleF(
            Bounds.X + BorderInset,
            Bounds.Y + HeaderHeight,
            Bounds.Width  - BorderInset * 2f,
            Bounds.Height - HeaderHeight - BorderInset);

        // ── Size persistence ──────────────────────────────────────────────────
        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetSingle("BW", Bounds.Width);
            writer.SetSingle("BH", Bounds.Height);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            bool ok = base.Read(reader);
            try
            {
                float w = reader.GetSingle("BW");
                float h = reader.GetSingle("BH");
                if (w > 0 && h > 0)
                    Bounds = new RectangleF(Pivot.X, Pivot.Y, w, h);
            }
            catch { /* first load — no saved size yet */ }
            return ok;
        }

        // ── Entry points called by Watch_Component ────────────────────────────

        // Called by RemovedFromDocument — destroy the panel before GH discards the component.
        public void DestroyPanel() => _panelHost.Destroy(Instances.ActiveCanvas);

        // Called via Task.Run from SolveInstance — serializes on the background thread,
        // then hands the result to PanelHost which buffers and flushes it.
        public async Task UpdateWebView(IGH_StructureEnumerator goo_structure)
        {
            if (goo_structure == null) return;

            var data = goo_structure.SerializeObjects();
            if (data.Count == 0) return;

            _panelHost.Store(data);
        }
    }
}
