﻿using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace AeroWizard
{
	/// <summary>
	/// Represents a single page in a <see cref="WizardControl"/>.
	/// </summary>
	[Designer("Design.WizardPageDesigner"), DesignTimeVisible(true)]
	[DefaultProperty("Text"), DefaultEvent("Commit")]
	[ToolboxItem(false)]
	public partial class WizardPage : Control
	{
		private bool initializing = false;
		private bool allowCancel = true, allowNext = true, allowBack = true;
		private bool showCancel = true, showNext = true;
		private bool isFinishPage = false;

		/// <summary>
		/// Initializes a new instance of the <see cref="WizardPage"/> class.
		/// </summary>
		public WizardPage()
		{
			initializing = true;
			InitializeComponent();
			Margin = Padding.Empty;
			Suppress = false;
			base.Text = Properties.Resources.WizardHeader;
			initializing = false;
		}

		/// <summary>
		/// Occurs when the user has clicked the Next/Finish button but before the page is changed.
		/// </summary>
		[Category("Wizard"), Description("Occurs when the user has clicked the Next/Finish button but before the page is changed")]
		public event EventHandler<WizardPageConfirmEventArgs> Commit;

		/// <summary>
		/// Occurs when this page is entered.
		/// </summary>
		[Category("Wizard"), Description("Occurs when this page is entered")]
		public event EventHandler<WizardPageInitEventArgs> Initialize;

		/// <summary>
		/// Occurs when the user has clicked the Back button but before the page is changed.
		/// </summary>
		[Category("Wizard"), Description("Occurs when the user has clicked the Back button")]
		public event EventHandler<WizardPageConfirmEventArgs> Rollback;

		/// <summary>
		/// Gets or sets a value indicating whether to enable the Back button.
		/// </summary>
		/// <value><c>true</c> if Back button is enabled; otherwise, <c>false</c>.</value>
		[DefaultValue(true), Category("Behavior"), Description("Indicates whether to enable the Back button")]
		public virtual bool AllowBack
		{
			get { return allowBack; }
			set
			{
				if (allowBack != value)
				{
					allowBack = value;
					if (Owner != null && this == Owner.SelectedPage)
						Owner.UpdateButtons();
				}
			}
		}

		/// <summary>
		/// Gets or sets a value indicating whether to enable the Cancel button.
		/// </summary>
		/// <value><c>true</c> if Cancel button is enabled; otherwise, <c>false</c>.</value>
		[DefaultValue(true), Category("Behavior"), Description("Indicates whether to enable the Cancel button")]
		public virtual bool AllowCancel
		{
			get { return allowCancel; }
			set
			{
				if (allowCancel != value)
				{
					allowCancel = value;
					if (Owner != null && this == Owner.SelectedPage)
						Owner.UpdateButtons();
				}
			}
		}

		/// <summary>
		/// Gets or sets a value indicating whether to enable the Next/Finish button.
		/// </summary>
		/// <value><c>true</c> if Next/Finish button is enabled; otherwise, <c>false</c>.</value>
		[DefaultValue(true), Category("Behavior"), Description("Indicates whether to enable the Next/Finish button")]
		public virtual bool AllowNext
		{
			get { return allowNext; }
			set
			{
				if (allowNext != value)
				{
					allowNext = value;
					if (Owner != null && this == Owner.SelectedPage)
						Owner.UpdateButtons();
				}
			}
		}

		/// <summary>
		/// Gets or sets a value indicating whether this page is the last page in the sequence and should display the Finish text instead of the Next text on the Next/Finish button.
		/// </summary>
		/// <value><c>true</c> if this page is a finish page; otherwise, <c>false</c>.</value>
		[DefaultValue(false), Category("Behavior"), Description("Indicates whether this page is the last page")]
		public virtual bool IsFinishPage
		{
			get { return isFinishPage; }
			set
			{
				if (isFinishPage != value)
				{
					isFinishPage = value;
					if (Owner != null && this == Owner.SelectedPage)
						Owner.UpdateButtons();
				}
			}
		}

		/// <summary>
		/// Gets or sets the next page that should be used when the user clicks the Next button or when the <see cref="WizardControl.NextPage()"/> method is called. This is used to override the default behavior of going to the next page in the sequence defined within the <see cref="WizardControl.Pages"/> collection.
		/// </summary>
		/// <value>The wizard page to go to.</value>
		[DefaultValue(null), Category("Behavior"),
		Description("Specify a page other than the next page in the Pages collection as the next page.")]
		public virtual WizardPage NextPage { get; set; }

		/// <summary>
		/// Gets the <see cref="WizardControl"/> for this page.
		/// </summary>
		/// <value>The <see cref="WizardControl"/> for this page.</value>
		[Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public virtual WizardControl Owner { get; internal set; }

		/// <summary>
		/// Gets or sets a value indicating whether to show the Cancel button. If both <see cref="ShowCancel"/> and <see cref="ShowNext"/> are <c>false</c>, then the bottom command area will not be shown.
		/// </summary>
		/// <value><c>true</c> if Cancel button should be shown; otherwise, <c>false</c>.</value>
		[DefaultValue(true), Category("Behavior"), Description("Indicates whether to show the Cancel button")]
		public virtual bool ShowCancel
		{
			get { return showCancel; }
			set
			{
				if (showCancel != value)
				{
					showCancel = value;
					if (Owner != null && this == Owner.SelectedPage)
						Owner.UpdateButtons();
				}
			}
		}

		/// <summary>
		/// Gets or sets a value indicating whether to show the Next/Finish button. If both <see cref="ShowCancel"/> and <see cref="ShowNext"/> are <c>false</c>, then the bottom command area will not be shown.
		/// </summary>
		/// <value><c>true</c> if Next/Finish button should be shown; otherwise, <c>false</c>.</value>
		[DefaultValue(true), Category("Behavior"), Description("Indicates whether to show the Next/Finish button")]
		public virtual bool ShowNext
		{
			get { return showNext; }
			set
			{
				if (showNext != value)
				{
					showNext = value;
					if (Owner != null && this == Owner.SelectedPage)
						Owner.UpdateButtons();
				}
			}
		}

		/// <summary>
		/// Gets or sets the height and width of the control.
		/// </summary>
		/// <value></value>
		/// <returns>
		/// The <see cref="T:System.Drawing.Size"/> that represents the height and width of the control in pixels.
		/// </returns>
		[Browsable(false)]
		public new System.Drawing.Size Size { get { return base.Size; } set { base.Size = value; } }

		/// <summary>
		/// Gets or sets a value indicating whether this <see cref="WizardPage"/> is suppressed and not shown in the normal flow.
		/// </summary>
		/// <value>
		///   <c>true</c> if suppressed; otherwise, <c>false</c>.
		/// </value>
		[DefaultValue(false), Category("Behavior"), Description("Suppresses this page from viewing if selected as next.")]
		public bool Suppress { get; set; }

		/// <summary>
		/// Returns a <see cref="System.String"/> that represents this wizard page.
		/// </summary>
		/// <returns>
		/// A <see cref="System.String"/> that represents this wizard page.
		/// </returns>
		public override string ToString()
		{
			return string.Format("{0} (\"{1}\")", this.Name, this.Text);
		}

		internal bool CommitPage()
		{
			return OnCommit();
		}

		internal void InitializePage(WizardPage prevPage)
		{
			OnInitialize(prevPage);
		}

		internal bool RollbackPage()
		{
			return OnRollback();
		}

		/// <summary>
		/// Raises the <see cref="Commit"/> event.
		/// </summary>
		/// <returns></returns>
		protected virtual bool OnCommit()
		{
			EventHandler<WizardPageConfirmEventArgs> handler = Commit;
			WizardPageConfirmEventArgs e =  new WizardPageConfirmEventArgs(this);
			if (handler != null)
				handler(this,e);
			return !e.Cancel;
		}

		/// <summary>
		/// Raises the <see cref="E:System.Windows.Forms.Control.GotFocus"/> event.
		/// </summary>
		/// <param name="e">An <see cref="T:System.EventArgs"/> that contains the event data.</param>
		protected override void OnGotFocus(EventArgs e)
		{
			base.OnGotFocus(e);
			Control firstChild = this.GetNextControl(this, true);
			if (firstChild != null)
				firstChild.Focus();
		}

		/// <summary>
		/// Raises the <see cref="Initialize"/> event.
		/// </summary>
		/// <param name="prevPage">The page that was previously selected.</param>
		protected virtual void OnInitialize(WizardPage prevPage)
		{
			EventHandler<WizardPageInitEventArgs> handler = Initialize;
			WizardPageInitEventArgs e = new WizardPageInitEventArgs(this, prevPage);
			if (handler != null)
				handler(this, e);
		}

		/// <summary>
		/// Raises the <see cref="Rollback"/> event.
		/// </summary>
		/// <returns></returns>
		protected virtual bool OnRollback()
		{
			EventHandler<WizardPageConfirmEventArgs> handler = Rollback;
			WizardPageConfirmEventArgs e = new WizardPageConfirmEventArgs(this);
			if (handler != null)
				handler(this, e);
			return !e.Cancel;
		}

		/// <summary>
		/// Raises the <see cref="E:System.Windows.Forms.Control.TextChanged"/> event.
		/// </summary>
		/// <param name="e">An <see cref="T:System.EventArgs"/> that contains the event data.</param>
		protected override void OnTextChanged(EventArgs e)
		{
			if (!initializing && Owner != null && Owner.SelectedPage == this)
				Owner.HeaderText = base.Text;
		}
	}

	/// <summary>
	/// Arguments supplied to the <see cref="WizardPage"/> events.
	/// </summary>
	public class WizardPageConfirmEventArgs : EventArgs
	{
		internal WizardPageConfirmEventArgs(WizardPage page)
		{
			Cancel = false;
			Page = page;
		}

		/// <summary>
		/// Gets or sets a value indicating whether this action is to be cancelled or allowed.
		/// </summary>
		/// <value><c>true</c> if cancel; otherwise, <c>false</c> to allow. Default is <c>false</c>.</value>
		[DefaultValue(false)]
		public bool Cancel { get; set; }

		/// <summary>
		/// Gets the <see cref="WizardPage"/> that has raised the event.
		/// </summary>
		/// <value>The wizard page.</value>
		public WizardPage Page { get; private set; }
	}

	/// <summary>
	/// Arguments supplied to the <see cref="WizardPage.Initialize"/> event.
	/// </summary>
	public class WizardPageInitEventArgs : WizardPageConfirmEventArgs
	{
		internal WizardPageInitEventArgs(WizardPage page, WizardPage prevPage)
			: base(page)
		{
			PreviousPage = prevPage;
		}

		/// <summary>
		/// Gets the <see cref="WizardPage"/> that was previously selected when the event was raised.
		/// </summary>
		/// <value>The previous wizard page.</value>
		public WizardPage PreviousPage { get; private set; }
	}
}