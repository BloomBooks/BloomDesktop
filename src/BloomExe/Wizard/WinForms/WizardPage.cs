using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace Bloom.Wizard.WinForms
{
	class WizardPage : Control
	{
		public event System.EventHandler<WizardPageInitEventArgs> Initialize;

		internal event System.EventHandler<EventArgs> AllowNextChanged;

		bool _allowNext;
		Label TitelLabel;
		Panel PagePanel;

		public WizardPage()
		{
			AllowNext = true;
			BackColor = Color.White;

			var titelPanel = new Panel {
				Padding = new Padding(10),
				AutoSize = true,
				Dock = DockStyle.Top,
			};
			TitelLabel = new Label {
				Padding = new Padding(10),
				AutoSize = true,
				Font = new Font(new FontFamily("Arial"), 14),
				Dock = DockStyle.Top,
				ForeColor = Color.SteelBlue
			};
			titelPanel.Controls.Add(TitelLabel);
			PagePanel = new Panel {
				Padding = new Padding(10),
				AutoSize = true,
				Dock = DockStyle.Fill
			};
			Controls.Add(PagePanel);
			Controls.Add(titelPanel);
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
			get;
			internal set;
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
				TitelLabel.Text = value;
			}
		}

		public void AddControls(Control[] controls)
		{
			if (controls.Length == 1)
			{
				controls[0].Dock = DockStyle.Fill;
				controls[0].AutoSize = true;
			}
			PagePanel.Controls.AddRange(controls);
		}
	}

	// TODO: move to own file
	class WizardPageInitEventArgs : EventArgs
	{

	}
}
