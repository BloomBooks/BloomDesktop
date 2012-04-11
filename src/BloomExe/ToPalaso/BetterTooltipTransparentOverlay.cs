using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Windows.Forms;

namespace Bloom.ToPalaso
{
	class BetterTooltipTransparentOverlay : ContainerControl
	{
		public BetterTooltipTransparentOverlay()
		{
			// Disable painting the background.
			this.SetStyle(ControlStyles.Opaque, true);
			this.UpdateStyles();

			// Make sure to set the AutoScaleMode property to None
			// so that the location and size property don't automatically change
			// when placed in a form that has different font than this.
			this.AutoScaleMode = AutoScaleMode.None;

			// Tab stop on a transparent sheet makes no sense.
			this.TabStop = false;
		}

		private const short WS_EX_TRANSPARENT = 0x20;

		protected override CreateParams CreateParams
		{
			[SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
			get
			{
				CreateParams l_cp;
				l_cp = base.CreateParams;
				l_cp.ExStyle = (l_cp.ExStyle | WS_EX_TRANSPARENT);
				return l_cp;
			}
		}
	}}
