﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

using Microsoft.Win32.DesktopWindowManager;

namespace AeroWizard
{
	/// <summary>
	/// A Label containing some text that will be drawn with glowing border on top of the Glass Sheet effect.
	/// </summary>
	//[Designer("AeroWizard.Design.ThemedLabelDesigner")]
	[DefaultProperty("Text")]
	internal class ThemedLabel : Label
	{
		private static bool isMin6;

		static ThemedLabel()
		{
			isMin6 = System.Environment.OSVersion.Version.Major >= 6;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ThemedLabel"/> class.
		/// </summary>
		public ThemedLabel()
		{
			this.SetStyle(ControlStyles.SupportsTransparentBackColor |
				ControlStyles.OptimizedDoubleBuffer |
				ControlStyles.AllPaintingInWmPaint |
				ControlStyles.ResizeRedraw |
				ControlStyles.UserPaint, true);

			BackColor = Color.Transparent;
		}

		/// <summary>
		/// Gets or sets the background color for the control.
		/// </summary>
		/// <value></value>
		/// <returns>
		/// A <see cref="T:System.Drawing.Color"/> that represents the background color of the control. The default is the value of the <see cref="P:System.Windows.Forms.Control.DefaultBackColor"/> property.
		/// </returns>
		/// <PermissionSet>
		/// 	<IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" Unrestricted="true"/>
		/// </PermissionSet>
		[DefaultValue(typeof(Color), "Transparent")]
		public override Color BackColor
		{
			get { return base.BackColor; }
			set { base.BackColor = value; }
		}

		/// <summary>
		/// Gets or sets the image that is displayed on a <see cref="T:System.Windows.Forms.Label"/>.
		/// </summary>
		/// <value></value>
		/// <returns>
		/// An <see cref="T:System.Drawing.Image"/> displayed on the <see cref="T:System.Windows.Forms.Label"/>. The default is null.
		/// </returns>
		/// <PermissionSet>
		/// 	<IPermission class="System.Security.Permissions.EnvironmentPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" Unrestricted="true"/>
		/// 	<IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" Unrestricted="true"/>
		/// 	<IPermission class="System.Security.Permissions.SecurityPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" Flags="UnmanagedCode, ControlEvidence"/>
		/// 	<IPermission class="System.Diagnostics.PerformanceCounterPermission, System, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" Unrestricted="true"/>
		/// </PermissionSet>
		[DefaultValue((Image)null)]
		public new Image Image
		{
			get { return base.Image; }
			set
			{
				base.Image = value;
				base.ImageIndex = -1;
				base.ImageList = null;
			}
		}

		/// <summary>
		/// </summary>
		/// <param name="proposedSize">The custom-sized area for a control.</param>
		/// <returns>
		/// An ordered pair of type <see cref="T:System.Drawing.Size"/> representing the width and height of a rectangle.
		/// </returns>
		public override Size GetPreferredSize(Size proposedSize)
		{
			Size sz = base.GetPreferredSize(proposedSize);
			if (Text.Length > 0)
				sz.Width += 10;
			return sz;
		}

		internal static Rectangle DeflateRect(Rectangle rect, Padding padding)
		{
			rect.X += padding.Left;
			rect.Y += padding.Top;
			rect.Width -= padding.Horizontal;
			rect.Height -= padding.Vertical;
			return rect;
		}

		/// <summary>
		/// </summary>
		/// <param name="e">A <see cref="T:System.Windows.Forms.PaintEventArgs"/> that contains the event data.</param>
		protected override void OnPaint(PaintEventArgs e)
		{
			if (Visible)
			{
				VisualStyleRenderer vs = null;
				if (Application.RenderWithVisualStyles || DesktopWindowManager.IsCompositionEnabled())
				{
					vs = new VisualStyleRenderer(VisualStyleElement.Window.Caption.Active);
					vs.DrawParentBackground(e.Graphics, base.ClientRectangle, this);
				}

				// Draw image
				Rectangle r = DeflateRect(base.ClientRectangle, base.Padding);
				if (this.Image != null)
				{
					Rectangle ir = CalcImageRenderBounds(this.Image, r, base.RtlTranslateAlignment(this.ImageAlign));
					if (this.ImageList != null && this.ImageIndex == 0)
					{
						if (vs != null & !this.IsDesignMode() & DesktopWindowManager.IsCompositionEnabled())
							vs.DrawGlassIcon(e.Graphics, r, this.ImageList, this.ImageIndex);
						else
							this.ImageList.Draw(e.Graphics, r.X, r.Y, r.Width, r.Height, this.ImageIndex);
					}
					else
					{
						if (vs != null & !this.IsDesignMode() & DesktopWindowManager.IsCompositionEnabled())
							vs.DrawGlassImage(e.Graphics, r, this.Image);
						else
							e.Graphics.DrawImage(this.Image, r);
					}
				}

				// Draw text
				if (this.Text.Length > 0)
				{
					TextFormatFlags tff = CreateTextFormatFlags(this.TextAlign, this.AutoEllipsis, this.UseMnemonic);
					if (this.IsDesignMode() || vs == null || !DesktopWindowManager.IsCompositionEnabled())
					{
						Brush br = DesktopWindowManager.IsCompositionEnabled() ? SystemBrushes.ActiveCaptionText : SystemBrushes.ControlText;
						e.Graphics.DrawString(Text, Font, br, base.ClientRectangle);
					}
					else
						vs.DrawGlowingText(e.Graphics, base.ClientRectangle, Text, Font, ForeColor, tff);
				}
			}
		}

		/// <summary>
		/// </summary>
		/// <param name="m">The Windows <see cref="T:System.Windows.Forms.Message"/> to process.</param>
		protected override void WndProc(ref Message m)
		{
			const int WM_NCHITTEST = 0x84;
			const int HTTRANSPARENT = -1;

			base.WndProc(ref m);
			if (m.Msg == WM_NCHITTEST)
				m.Result = new IntPtr(HTTRANSPARENT);
		}

		private TextFormatFlags CreateTextFormatFlags(System.Drawing.ContentAlignment textAlign, bool showEllipsis, bool useMnemonic)
		{
			TextFormatFlags flags = TextFormatFlags.GlyphOverhangPadding | TextFormatFlags.SingleLine;
			if ((textAlign & (System.Drawing.ContentAlignment.BottomRight | System.Drawing.ContentAlignment.BottomCenter | System.Drawing.ContentAlignment.BottomLeft)) != ((System.Drawing.ContentAlignment)0))
				flags |= TextFormatFlags.Bottom;
			if ((textAlign & (System.Drawing.ContentAlignment.MiddleRight | System.Drawing.ContentAlignment.MiddleCenter | System.Drawing.ContentAlignment.MiddleLeft)) != ((System.Drawing.ContentAlignment)0))
				flags |= TextFormatFlags.VerticalCenter;
			if ((textAlign & (System.Drawing.ContentAlignment.BottomRight | System.Drawing.ContentAlignment.MiddleRight | System.Drawing.ContentAlignment.TopRight)) != ((System.Drawing.ContentAlignment)0))
				flags |= TextFormatFlags.Right;
			if ((textAlign & (System.Drawing.ContentAlignment.BottomCenter | System.Drawing.ContentAlignment.MiddleCenter | System.Drawing.ContentAlignment.TopCenter)) != ((System.Drawing.ContentAlignment)0))
				flags |= TextFormatFlags.HorizontalCenter;
			if (showEllipsis)
				flags |= TextFormatFlags.EndEllipsis;
			if (this.RightToLeft == RightToLeft.Yes)
				flags |= TextFormatFlags.RightToLeft;
			if (!useMnemonic)
				return (flags | TextFormatFlags.NoPrefix);
			if (!this.ShowKeyboardCues)
				flags |= TextFormatFlags.HidePrefix;
			return flags;
		}
	}
}