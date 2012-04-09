using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace Messir.Windows.Forms
{
	/// <summary>
	/// Represents a renderer class for TabStrip control
	/// </summary>
	internal class TabStripRenderer : ToolStripRenderer
	{
		private const int selOffset = 2;

		private ToolStripRenderer _currentRenderer = null;
		private ToolStripRenderMode _renderMode = ToolStripRenderMode.Custom;
		private bool _mirrored = false;
		private bool _useVisualStyles = Application.RenderWithVisualStyles;

		/// <summary>
		/// Gets or sets render mode for this renderer
		/// </summary>
		public ToolStripRenderMode RenderMode
		{
			get { return _renderMode; }
			set
			{
				_renderMode = value;
				switch (_renderMode)
				{
					case ToolStripRenderMode.Professional:
						_currentRenderer = new ToolStripProfessionalRenderer();
						break;
					case ToolStripRenderMode.System:
						_currentRenderer = new ToolStripSystemRenderer();
						break;
					default:
						_currentRenderer = null;
						break;
				}
			}
		}

		/// <summary>
		/// Gets or sets whether to mirror background
		/// </summary>
		/// <remarks>Use false for left and top positions, true for right and bottom</remarks>
		public bool Mirrored
		{
			get { return _mirrored; }
			set { _mirrored = value; }
		}

		/// <summary>
		/// Returns if visual styles should be applied for drawing
		/// </summary>
		public bool UseVisualStyles
		{
			get { return _useVisualStyles; }
			set
			{
				if (value && !Application.RenderWithVisualStyles)
					return;
				_useVisualStyles = value;
			}
		}

		protected override void Initialize(ToolStrip ts)
		{
			base.Initialize(ts);
		}

		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
		{
			return;
			Color c = SystemColors.AppWorkspace;
			if (UseVisualStyles)
			{
				VisualStyleRenderer rndr = new VisualStyleRenderer(VisualStyleElement.Tab.Pane.Normal);
				c = rndr.GetColor(ColorProperty.BorderColorHint);
			}

			using (Pen p = new Pen(c))
			using (Pen p2 = new Pen(e.BackColor))
			{
				Rectangle r = e.ToolStrip.Bounds;
				int x1 = (Mirrored) ? 0 : r.Width - 1 - e.ToolStrip.Padding.Horizontal;
				int y1 = (Mirrored) ? 0 : r.Height - 1;
				if (e.ToolStrip.Orientation == Orientation.Horizontal)
					e.Graphics.DrawLine(p, 0, y1, r.Width, y1);
				else
				{
					e.Graphics.DrawLine(p, x1, 0, x1, r.Height);
					if (!Mirrored)
						for (int i = x1 + 1; i < r.Width; i++)
							e.Graphics.DrawLine(p2, i, 0, i, r.Height);
				}
				foreach (ToolStripItem x in e.ToolStrip.Items)
				{
					if (x.IsOnOverflow) continue;
					TabStripButton btn = x as TabStripButton;
					if (btn == null) continue;
					Rectangle rc = btn.Bounds;
					int x2 = (Mirrored) ? rc.Left : rc.Right;
					int y2 = (Mirrored) ? rc.Top : rc.Bottom - 1;
					int addXY = (Mirrored) ? 0 : 1;
					if (e.ToolStrip.Orientation == Orientation.Horizontal)
					{
						e.Graphics.DrawLine(p, rc.Left, y2, rc.Right, y2);
						if (btn.Checked) e.Graphics.DrawLine(p2, rc.Left + 2 - addXY, y2, rc.Right - 2 - addXY, y2);
					}
					else
					{
						e.Graphics.DrawLine(p, x2, rc.Top, x2, rc.Bottom);
						if (btn.Checked) e.Graphics.DrawLine(p2, x2, rc.Top + 2 - addXY, x2, rc.Bottom - 2 - addXY);
					}
				}
			}
		}

		protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawToolStripBackground(e);
			else
				base.OnRenderToolStripBackground(e);
		}

		protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
		{
			Graphics g = e.Graphics;
			TabStrip tabs = e.ToolStrip as TabStrip;
			TabStripButton tab = e.Item as TabStripButton;
			if (tabs == null || tab == null)
			{
				if (_currentRenderer != null)
					_currentRenderer.DrawButtonBackground(e);
				else
					base.OnRenderButtonBackground(e);
				return;
			}

			bool selected = tab.Checked;
			bool hovered = tab.Selected;
			int top = 0;
			int left = 0;
			int width = tab.Bounds.Width - 1;
			int height = tab.Bounds.Height - 1;
			Rectangle drawBorder;

//
//			if (UseVisualStyles)
//			{
//				if (tabs.Orientation == Orientation.Horizontal)
//				{
//					if (!selected)
//					{
//						top = selOffset;
//						height -= (selOffset - 1);
//					}
//					else
//						top = 1;
//					drawBorder = new Rectangle(0, 0, width, height);
//				}
//				else
//				{
//					if (!selected)
//					{
//						left = selOffset;
//						width -= (selOffset - 1);
//					}
//					else
//						left = 1;
//					drawBorder = new Rectangle(0, 0, height, width);
//				}
//				using (Bitmap b = new Bitmap(drawBorder.Width, drawBorder.Height))
//				{
//					VisualStyleElement el = VisualStyleElement.Tab.TabItem.Normal;
//					if (selected)
//						el = VisualStyleElement.Tab.TabItem.Pressed;
//					if (hovered)
//						el = VisualStyleElement.Tab.TabItem.Hot;
//					if (!tab.Enabled)
//						el = VisualStyleElement.Tab.TabItem.Disabled;
//
//					if (!selected || hovered) drawBorder.Width++; else drawBorder.Height++;
//
//					using (Graphics gr = Graphics.FromImage(b))
//					{
//						VisualStyleRenderer rndr = new VisualStyleRenderer(el);
//						rndr.DrawBackground(gr, drawBorder);
//
//						if (tabs.Orientation == Orientation.Vertical)
//						{
//							if (Mirrored)
//								b.RotateFlip(RotateFlipType.Rotate270FlipXY);
//							else
//								b.RotateFlip(RotateFlipType.Rotate270FlipNone);
//						}
//						else
//						{
//							if (Mirrored)
//								b.RotateFlip(RotateFlipType.RotateNoneFlipY);
//						}
//						if (Mirrored)
//						{
//							left = tab.Bounds.Width - b.Width - left;
//							top = tab.Bounds.Height - b.Height - top;
//						}
//						g.DrawImage(b, left, top);
//					}
//				}
//			}
//			else
			{
				if (tabs.Orientation == Orientation.Horizontal)
				{
					if (!selected)
					{
						top = selOffset;
						height -= (selOffset - 1);
					}
					else
						top = 1;
					if (Mirrored)
					{
						left = 1;
						top = 0;
					}
					else
						top++;
					width--;
				}
//				else
//				{
//					if (!selected)
//					{
//						left = selOffset;
//						width--;
//					}
//					else
//						left = 1;
//					if (Mirrored)
//					{
//						left = 0;
//						top = 1;
//					}
//				}
				height--;
				drawBorder = new Rectangle(left, top, width, height);


				using (GraphicsPath gp = new GraphicsPath())
				{
//					if (Mirrored && tabs.Orientation == Orientation.Horizontal)
//					{
//						gp.AddLine(drawBorder.Left, drawBorder.Top, drawBorder.Left, drawBorder.Bottom - 2);
//						gp.AddArc(drawBorder.Left, drawBorder.Bottom - 3, 2, 2, 90, 90);
//						gp.AddLine(drawBorder.Left + 2, drawBorder.Bottom, drawBorder.Right - 2, drawBorder.Bottom);
//						gp.AddArc(drawBorder.Right - 2, drawBorder.Bottom - 3, 2, 2, 0, 90);
//						gp.AddLine(drawBorder.Right, drawBorder.Bottom - 2, drawBorder.Right, drawBorder.Top);
//					}
					//else
					 if (!Mirrored && tabs.Orientation == Orientation.Horizontal)
					{
						gp.AddLine(drawBorder.Left, drawBorder.Bottom, drawBorder.Left, drawBorder.Top + 2);
						gp.AddArc(drawBorder.Left, drawBorder.Top + 1, 2, 2, 180, 90);
						gp.AddLine(drawBorder.Left + 2, drawBorder.Top, drawBorder.Right - 2, drawBorder.Top);
						gp.AddArc(drawBorder.Right - 2, drawBorder.Top + 1, 2, 2, 270, 90);
						gp.AddLine(drawBorder.Right, drawBorder.Top + 2, drawBorder.Right, drawBorder.Bottom);
					}
//					else if (Mirrored && tabs.Orientation == Orientation.Vertical)
//					{
//						gp.AddLine(drawBorder.Left, drawBorder.Top, drawBorder.Right - 2, drawBorder.Top);
//						gp.AddArc(drawBorder.Right - 2, drawBorder.Top + 1, 2, 2, 270, 90);
//						gp.AddLine(drawBorder.Right, drawBorder.Top + 2, drawBorder.Right, drawBorder.Bottom - 2);
//						gp.AddArc(drawBorder.Right - 2, drawBorder.Bottom - 3, 2, 2, 0, 90);
//						gp.AddLine(drawBorder.Right - 2, drawBorder.Bottom, drawBorder.Left, drawBorder.Bottom);
//					}
//					else
//					{
//						gp.AddLine(drawBorder.Right, drawBorder.Top, drawBorder.Left + 2, drawBorder.Top);
//						gp.AddArc(drawBorder.Left, drawBorder.Top + 1, 2, 2, 180, 90);
//						gp.AddLine(drawBorder.Left, drawBorder.Top + 2, drawBorder.Left, drawBorder.Bottom - 2);
//						gp.AddArc(drawBorder.Left, drawBorder.Bottom - 3, 2, 2, 90, 90);
//						gp.AddLine(drawBorder.Left + 2, drawBorder.Bottom, drawBorder.Right, drawBorder.Bottom);
//					}

					if (selected || hovered)
					{
						Color fill = (hovered) ? Color.WhiteSmoke : Color.FromArgb(64, 64, 64);
						if (_renderMode == ToolStripRenderMode.Professional)
						{
							fill = (hovered) ? ProfessionalColors.ButtonCheckedGradientBegin : ProfessionalColors.ButtonCheckedGradientEnd;
							using (LinearGradientBrush br = new LinearGradientBrush(tab.ContentRectangle, fill, ProfessionalColors.ButtonCheckedGradientMiddle, LinearGradientMode.Vertical))
								g.FillPath(br, gp);
						}
						else
							using (SolidBrush br = new SolidBrush(fill))
								g.FillPath(br, gp);
					}
					else
					{
						using (SolidBrush br = new SolidBrush(e.Item.BackColor))
							g.FillPath(br, gp);
					}

//					using (Pen p = new Pen((selected) ? ControlPaint.Dark(SystemColors.AppWorkspace) : SystemColors.AppWorkspace))
//						g.DrawPath(p, gp);

					g.DrawPath(Pens.Black, gp);
				}
			}

		}

		protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
		{
			Rectangle rc = e.ImageRectangle;
			TabStripButton btn = e.Item as TabStripButton;
			if (btn != null)
			{
				int delta = ((Mirrored) ? -1 : 1) * ((btn.Checked) ? 1 : selOffset);
				if (e.ToolStrip.Orientation == Orientation.Horizontal)
					rc.Offset((Mirrored) ? 2 : 1, delta + ((Mirrored) ? 1 : 0));
				else
					rc.Offset(delta + 2, 0);
			}
			ToolStripItemImageRenderEventArgs x =
				new ToolStripItemImageRenderEventArgs(e.Graphics, e.Item, e.Image, rc);
			if (_currentRenderer != null)
				_currentRenderer.DrawItemImage(x);
			else
				base.OnRenderItemImage(x);
		}


		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			Rectangle rc = e.TextRectangle;
			TabStripButton btn = e.Item as TabStripButton;

			Color c = e.TextColor;
			Font f = e.TextFont;

			rc.Offset(0, 6);//hatton for bloom lower is better

			if (btn != null)
			{
				int delta = ((Mirrored) ? -1 : 1) * ((btn.Checked) ? 1 : selOffset);
				if (e.ToolStrip.Orientation == Orientation.Horizontal)
					rc.Offset((Mirrored) ? 2 : 1, delta + ((Mirrored) ? 1 : -1));
				else
					rc.Offset(delta + 2, 0);
				if (btn.Selected)
					c = btn.HotTextColor;
				else if (btn.Checked)
					c = btn.SelectedTextColor;
				if (btn.Checked)
					f = btn.SelectedFont;
			}
			ToolStripItemTextRenderEventArgs x =
				new ToolStripItemTextRenderEventArgs(e.Graphics, e.Item, e.Text, rc, c, f, e.TextFormat);
			x.TextDirection = e.TextDirection;
			if (_currentRenderer != null)
				_currentRenderer.DrawItemText(x);
			else
				base.OnRenderItemText(x);
		}

		protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawArrow(e);
			else
				base.OnRenderArrow(e);
		}

		protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawDropDownButtonBackground(e);
			else
				base.OnRenderDropDownButtonBackground(e);
		}

		protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawGrip(e);
			else
				base.OnRenderGrip(e);
		}

		protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawImageMargin(e);
			else
				base.OnRenderImageMargin(e);
		}

		protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawItemBackground(e);
			else
			{
				//base.OnRenderItemBackground(e);
				e.Graphics.FillRectangle(Brushes.BlueViolet, e.Item.ContentRectangle);
			}
		}

		protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawItemCheck(e);
			else
				base.OnRenderItemCheck(e);
		}

		protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawLabelBackground(e);
			else
				base.OnRenderLabelBackground(e);
		}

		protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawMenuItemBackground(e);
			else
				base.OnRenderMenuItemBackground(e);
		}

		protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawOverflowButtonBackground(e);
			else
				base.OnRenderOverflowButtonBackground(e);
		}

		protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawSeparator(e);
			else
				base.OnRenderSeparator(e);
		}

		protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawSplitButton(e);
			else
				base.OnRenderSplitButtonBackground(e);
		}

		protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawStatusStripSizingGrip(e);
			else
				base.OnRenderStatusStripSizingGrip(e);
		}

		protected override void OnRenderToolStripContentPanelBackground(ToolStripContentPanelRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawToolStripContentPanelBackground(e);
			else
				base.OnRenderToolStripContentPanelBackground(e);
		}

		protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawToolStripPanelBackground(e);
			else
				base.OnRenderToolStripPanelBackground(e);
		}

		protected override void OnRenderToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e)
		{
			if (_currentRenderer != null)
				_currentRenderer.DrawToolStripStatusLabelBackground(e);
			else
				base.OnRenderToolStripStatusLabelBackground(e);
		}
	}
}