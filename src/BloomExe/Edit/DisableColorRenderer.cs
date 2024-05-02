using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bloom.Edit
{
    /// <summary>
    /// Install this as the renderer for a ToolStrip to make it render text (and the down arrow
    /// in a ToolStripDropDownButton) in the specified color when an item is disabled.
    /// </summary>
    internal class DisableColorRenderer : ToolStripSystemRenderer
    {
        Color disabledColor;

        public DisableColorRenderer(Color disabledColor)
        {
            this.disabledColor = disabledColor;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Enabled)
            {
                base.OnRenderItemText(e);
                return;
            }

            // We have to actually take over drawing it, because when disabled, e.TextColor
            // is ignored. There doesn't seem to be a property for disabled text color.
            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                e.TextRectangle,
                disabledColor,
                e.TextFormat
            );
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (!e.Item.Enabled)
                e.ArrowColor = disabledColor;

            base.OnRenderArrow(e);
        }
    }
}
