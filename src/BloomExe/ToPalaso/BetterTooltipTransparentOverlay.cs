using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace Bloom.ToPalaso
{
	class BetterTooltipTransparentOverlay : ContainerControl
	{
		private readonly Control _overlayedControl;

		public BetterTooltipTransparentOverlay(Control overlayedControl)
		{
			_overlayedControl = overlayedControl;

			// We want our control to be transparent. However, this causes the parent
			// background color to be painted instead of the control we're overlaying, so we
			// need to do some special magic in OnPaint and draw the button we're overlaying.
			// Originally the code set the Opaque style to prevent the background from painting,
			// but on Linux that causes some pixels to not be redrawn when switching between
			// tabs.
			SetStyle(ControlStyles.SupportsTransparentBackColor, true);
			UpdateStyles();
			BackColor = Color.Transparent;

			// Make sure to set the AutoScaleMode property to None
			// so that the location and size property don't automatically change
			// when placed in a form that has different font than this.
			AutoScaleMode = AutoScaleMode.None;

			// Tab stop on a transparent sheet makes no sense.
			TabStop = false;
		}

		private const short WS_EX_TRANSPARENT = 0x20;

		protected override CreateParams CreateParams
		{
			[SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
			get
			{
				// If we don't set the WS_EX_TRANSPARENT style the overlayed buttons don't
				// paint properly on Windows.
				CreateParams createParams = base.CreateParams;
				createParams.ExStyle = (createParams.ExStyle | WS_EX_TRANSPARENT);
				return createParams;
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (Parent == null)
				return;

			// Draw the control we're overlaying. On Linux it might not show properly
			// otherwise, especially since it is disabled.
			using (var behind = new Bitmap(Parent.ClientSize.Width, Parent.ClientSize.Height))
			{
				_overlayedControl.DrawToBitmap(behind, _overlayedControl.Bounds);
				e.Graphics.DrawImage(behind, 0, 0);
			}
		}
	}
}
