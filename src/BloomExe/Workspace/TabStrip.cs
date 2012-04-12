using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using Bloom.Workspace;
using VisualStyles = System.Windows.Forms.VisualStyles;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Collections;
using System.Windows.Forms.Design;

namespace Messir.Windows.Forms
{
	public class SelectedTabChangedEventArgs : EventArgs
	{
		public readonly TabStripButton SelectedTab;

		public SelectedTabChangedEventArgs(TabStripButton tab)
		{
			SelectedTab = tab;
		}

	}

	/// <summary>
	/// Represents a TabStrip control
	/// </summary>
	public class TabStrip : ToolStrip
	{
		private TabStripRenderer myRenderer = new TabStripRenderer();
		protected TabStripButton mySelTab;
		DesignerVerb insPage = null;

		public TabStrip() : base()
		{
			InitControl();
		}

		public TabStrip(params TabStripButton[] buttons) : base(buttons)
		{
			InitControl();
		}

		protected void InitControl()
		{
			base.RenderMode = ToolStripRenderMode.ManagerRenderMode;
			base.Renderer = myRenderer;
			myRenderer.RenderMode = this.RenderStyle;
			insPage = new DesignerVerb("Insert tab page", new EventHandler(OnInsertPageClicked));
		}

		public override ISite Site
		{
			get
			{
				ISite site = base.Site;
				if (site != null && site.DesignMode)
				{
					IContainer comp = site.Container;
					if (comp != null)
					{
						IDesignerHost host = comp as IDesignerHost;
						if (host != null)
						{
							IDesigner designer = host.GetDesigner(site.Component);
							if (designer != null && !designer.Verbs.Contains(insPage))
								designer.Verbs.Add(insPage);
						}
					}
				}
				return site;
			}
			set
			{
				base.Site = value;
			}
		}

		protected void OnInsertPageClicked(object sender, EventArgs e)
		{
			ISite site = base.Site;
			if (site != null && site.DesignMode)
			{
				IContainer container = site.Container;
				if (container != null)
				{
					TabStripButton btn = new TabStripButton();
					container.Add(btn);
					btn.Text = btn.Name;
				}
			}
		}

		/// <summary>
		/// Gets custom renderer for TabStrip. Set operation has no effect
		/// </summary>
		public new ToolStripRenderer Renderer
		{
			get { return myRenderer; }
			set { base.Renderer = myRenderer; }
		}

		/// <summary>
		/// Gets or sets layout style for TabStrip control
		/// </summary>
		public new ToolStripLayoutStyle LayoutStyle
		{
			get { return base.LayoutStyle; }
			set
			{
				switch (value)
				{
					case ToolStripLayoutStyle.StackWithOverflow:
					case ToolStripLayoutStyle.HorizontalStackWithOverflow:
					case ToolStripLayoutStyle.VerticalStackWithOverflow:
						base.LayoutStyle = ToolStripLayoutStyle.StackWithOverflow;
						break;
					case ToolStripLayoutStyle.Table:
						base.LayoutStyle = ToolStripLayoutStyle.Table;
						break;
					case ToolStripLayoutStyle.Flow:
						base.LayoutStyle = ToolStripLayoutStyle.Flow;
						break;
					default:
						base.LayoutStyle = ToolStripLayoutStyle.StackWithOverflow;
						break;
				}
			}
		}

		/// <summary>
		///
		/// </summary>
		[Obsolete("Use RenderStyle instead")]
		[Browsable(false)]
		public new ToolStripRenderMode RenderMode
		{
			get { return base.RenderMode; }
			set { RenderStyle = value; }
		}

		/// <summary>
		/// Gets or sets render style for TabStrip, use it instead of
		/// </summary>
		[Category("Appearance")]
		[Description("Gets or sets render style for TabStrip. You should use this property instead of RenderMode.")]
		public ToolStripRenderMode RenderStyle
		{
			get { return myRenderer.RenderMode; }
			set
			{
				myRenderer.RenderMode = value;
				this.Invalidate();
			}
		}

		protected override Padding DefaultPadding
		{
			get
			{
				return Padding.Empty;
			}
		}

		[Browsable(false)]
		public new Padding Padding
		{
			get { return DefaultPadding; }
			set { }
		}

		/// <summary>
		/// Gets or sets if control should use system visual styles for painting items
		/// </summary>
		[Category("Appearance")]
		[Description("Specifies if TabStrip should use system visual styles for painting items")]
		public bool UseVisualStyles
		{
			get { return myRenderer.UseVisualStyles; }
			set
			{
				myRenderer.UseVisualStyles = value;
				this.Invalidate();
			}
		}

		/// <summary>
		/// Gets or sets if TabButtons should be drawn flipped
		/// </summary>
		[Category("Appearance")]
		[Description("Specifies if TabButtons should be drawn flipped (for right- and bottom-aligned TabStrips)")]
		public bool FlipButtons
		{
			get { return myRenderer.Mirrored; }
			set
			{
				myRenderer.Mirrored = value;
				this.Invalidate();
			}
		}

		/// <summary>
		/// Gets or sets currently selected tab
		/// </summary>
		public TabStripButton SelectedTab
		{
			get { return mySelTab; }
			set
			{
				if (value == null)
					return;
				if (mySelTab == value)
					return;
				if (value.Owner != this)
					throw new ArgumentException("Cannot select TabButtons that do not belong to this TabStrip");
				OnItemClicked(new ToolStripItemClickedEventArgs(value));
			}
		}

		public event EventHandler<SelectedTabChangedEventArgs> SelectedTabChanged;

		protected void OnTabSelected(TabStripButton tab)
		{
			this.Invalidate();
			if (SelectedTabChanged != null)
				SelectedTabChanged(this, new SelectedTabChangedEventArgs(tab));
		}

		protected override void OnItemAdded(ToolStripItemEventArgs e)
		{
			base.OnItemAdded(e);
			if (e.Item is TabStripButton)
				SelectedTab = (TabStripButton)e.Item;
		}

		protected override void OnItemClicked(ToolStripItemClickedEventArgs e)
		{
			TabStripButton clickedBtn = e.ClickedItem as TabStripButton;
			if (clickedBtn != null)
			{
				this.SuspendLayout();
				mySelTab = clickedBtn;
				this.ResumeLayout();
				OnTabSelected(clickedBtn);
			}
			base.OnItemClicked(e);
		}

	}

	/// <summary>
	/// Represents a TabButton for TabStrip control
	/// </summary>
	[ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip)]
	public class TabStripButton : ToolStripButton
	{
		public TabStripButton() : base() { InitButton(); }
		public TabStripButton(Image image) : base(image) { InitButton(); }
		public TabStripButton(string text) : base(text) { InitButton(); }
		public TabStripButton(string text, Image image) : base(text, image) { InitButton(); }
		public TabStripButton(string Text, Image Image, EventHandler Handler) : base(Text, Image, Handler) { InitButton(); }
		public TabStripButton(string Text, Image Image, EventHandler Handler, string name) : base(Text, Image, Handler, name) { InitButton(); }

		private void InitButton()
		{
			m_SelectedFont = this.Font;
		}

		public override Size GetPreferredSize(Size constrainingSize)
		{
			Size sz = base.GetPreferredSize(constrainingSize);
			if (this.Owner != null && this.Owner.Orientation == Orientation.Vertical)
			{
				sz.Width += 3;
				sz.Height += 10;
			}
			else
			{
				sz.Width += 3 + 30;
				sz.Height += 10 + 10;
			}
			return sz;
		}

		protected override Padding DefaultMargin
		{
			get
			{
				return new Padding(0);
			}
		}

		[Browsable(false)]
		public new Padding Margin
		{
			get { return base.Margin; }
			set { }
		}

		[Browsable(false)]
		public new Padding Padding
		{
			get { return base.Padding; }
			set { }
		}

		private Color m_HotTextColor = Control.DefaultForeColor;

		[Category("Appearance")]
		[Description("Top bar color when this tab is checked")]
		public Color BarColor { get; set; }

		[Category("Appearance")]
		[Description("Text color when TabButton is highlighted")]
		public Color HotTextColor
		{
			get { return m_HotTextColor; }
			set { m_HotTextColor = value; }
		}

		private Color m_SelectedTextColor = Control.DefaultForeColor;

		[Category("Appearance")]
		[Description("Text color when TabButton is selected")]
		public Color SelectedTextColor
		{
			get { return m_SelectedTextColor; }
			set { m_SelectedTextColor = value; }
		}

		private Font m_SelectedFont;

		[Category("Appearance")]
		[Description("Font when TabButton is selected")]
		public Font SelectedFont
		{
			get { return (m_SelectedFont == null) ? this.Font : m_SelectedFont; }
			set { m_SelectedFont = value; }
		}

		[Browsable(false)]
		[DefaultValue(false)]
		public new bool Checked
		{
			get { return IsSelected; }
			set { }
		}

		/// <summary>
		/// Gets or sets if this TabButton is currently selected
		/// </summary>
		[Browsable(false)]
		public bool IsSelected
		{
			get
			{
				TabStrip owner = Owner as TabStrip;
				if (owner != null)
					return (this == owner.SelectedTab);
				return false;
			}
			set
			{
				if (value == false) return;
				TabStrip owner = Owner as TabStrip;
				if (owner == null) return;
				owner.SelectedTab = this;
			}
		}

		protected override void OnOwnerChanged(EventArgs e)
		{
			if (Owner != null && !(Owner is TabStrip))
				throw new Exception("Cannot add TabStripButton to " + Owner.GetType().Name);
			base.OnOwnerChanged(e);
		}

	}

}
