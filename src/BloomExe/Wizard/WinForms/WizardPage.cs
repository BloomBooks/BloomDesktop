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


		public WizardPage()
		{
			AllowNext = true;
			BackColor = Color.White;
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

	}

	// TODO: move to own file
	class WizardPageInitEventArgs : EventArgs
	{

	}
}
