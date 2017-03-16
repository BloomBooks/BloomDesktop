using System;
using System.Windows.Forms;
using System.Drawing;

namespace Bloom.Wizard.WinForms
{
	class WizardPage : Control
	{
		public event System.EventHandler<WizardPageInitEventArgs> Initialize;

		internal event System.EventHandler<EventArgs> AllowNextChanged;

		bool _allowNext;
		private WizardPage _nextPage;
		Label TitleLabel;
		Panel PagePanel;

		public WizardPage()
		{
			AllowNext = true;
			BackColor = Color.White;

			var titlePanel = new Panel {
				Padding = new Padding(30, 30, 30, 0),
				AutoSize = true,
				Dock = DockStyle.Top,
			};
			TitleLabel = new Label {
				AutoSize = true,
				Font = new Font(new FontFamily("Arial"), 12),
				Dock = DockStyle.Top,
				ForeColor = Color.DarkBlue
			};
			titlePanel.Controls.Add(TitleLabel);
			PagePanel = new Panel {
				Padding = new Padding(30),
				AutoSize = true,
				Dock = DockStyle.Fill
			};
			Controls.Add(PagePanel);
			Controls.Add(titlePanel);
		}

		public bool Suppress
		{
			get;
			set;
		}

		public bool AllowNext
		{
			get { return _allowNext; }
			set
			{
				_allowNext = value;
				if(AllowNextChanged != null)
					AllowNextChanged(this, EventArgs.Empty);
			}
		}

		public WizardPage NextPage
		{
			get
			{
				if (_nextPage == null)
					return null;
				if (_nextPage.Suppress)
					return _nextPage.NextPage;
				return _nextPage;
			}
			internal set { _nextPage = value;  }
		}

		public bool IsFinishPage
		{
			get;
			set;
		}

		internal void InvokeInitializeEvent()
		{
			if (Initialize != null)
				Initialize(this, new WizardPageInitEventArgs());
		}

		public override string Text
		{
			get { return base.Text; }
			set
			{
				base.Text = value;
				TitleLabel.Text = value;
			}
		}

		public void AddControls(Control[] controls)
		{
			if (controls.Length == 1)
			{
				controls[0].AutoSize = true;
				controls[0].Dock = DockStyle.Fill;
			}
			PagePanel.Controls.AddRange(controls);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				foreach (Control control in Controls)
					control.Dispose();
			}
			base.Dispose(disposing);
		}
	}

	// TODO: move to own file
	class WizardPageInitEventArgs : EventArgs
	{

	}
}
