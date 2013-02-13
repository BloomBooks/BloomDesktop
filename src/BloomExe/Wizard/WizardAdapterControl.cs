using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Bloom.Wizard
{
	/// <summary>
	/// Override control to allow setting of the privite parentForm field at the appropiate time.
	/// </summary>
	class MyFixedAeroWizard : AeroWizard.WizardControl
	{
		protected override void OnParentChanged(EventArgs e)
		{
			base.OnParentChanged(e);

			FieldInfo parentFormField = typeof(AeroWizard.WizardControl).GetField("parentForm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			parentFormField.SetValue(this, this.FindForm());

			var form = this.FindForm();
		}
	}

	class WizardAdapterControl : Control, ISupportInitialize
	{
		protected AeroWizard.WizardControl _aeroWizard;

		public WizardAdapterControl()
		{
			_aeroWizard = new MyFixedAeroWizard();

			this.Dock = DockStyle.Fill;

			_aeroWizard.Cancelled += (sender, e) =>
				{
					if (this.Cancelled != null)
						this.Cancelled(sender, e);
				};

			_aeroWizard.Finished += (sender, e) =>
				{
					if (this.Finished != null)
						this.Finished(sender, e);
				};

			_aeroWizard.SelectedPageChanged += (sender, e) =>
				{
					if (this.SelectedPageChanged != null)
						this.SelectedPageChanged(sender, e);
				};
		}

		public WizardAdapterPage SelectedPage
		{
			get { return new WizardAdapterPage(_aeroWizard.SelectedPage); }
		}

		List<WizardAdapterPage> _pages;

		public List<WizardAdapterPage> Pages {
			get
			{
				if (_pages == null)
				{
					_pages = new List<WizardAdapterPage>();
					foreach (AeroWizard.WizardPage page in _aeroWizard.Pages)
					{
						_pages.Add(new WizardAdapterPage(page));
					}
				}

				return _pages;
			}

			protected set
			{
				throw new NotImplementedException();
			}
		}

		public string Title
		{
			get
			{
				return _aeroWizard.Title;
			}
			set
			{
				_aeroWizard.Title = value;
			}
		}

		public Icon TitleIcon
		{
			get
			{
				return _aeroWizard.TitleIcon;
			}
			set
			{
				_aeroWizard.TitleIcon = TitleIcon;
			}
		}

		public event EventHandler Cancelled;

		public event EventHandler Finished;

		public event EventHandler SelectedPageChanged;

		#region ISupportInitialize implementation
		void ISupportInitialize.BeginInit()
		{
			_aeroWizard.BeginInit();
		}

		void ISupportInitialize.EndInit()
		{
			foreach (WizardAdapterPage page in _pages)
			{
				page.AeroPage.Controls.AddRange(page.Controls.Cast<Control>().ToArray());

				_aeroWizard.Pages.Add(page.AeroPage);
			}

			_aeroWizard.ParentChanged += (s, e) =>
				{
					Console.WriteLine("parent changed");
				};

			this.Controls.Add(_aeroWizard);

			_aeroWizard.EndInit();

		}
		#endregion
	}

	class WizardAdapterPage : Control
	{
		AeroWizard.WizardPage _page;

		internal AeroWizard.WizardPage AeroPage
		{
			get
			{
				return _page;
			}
		}

		public WizardAdapterPage() : this(new AeroWizard.WizardPage())
		{

		}

		public WizardAdapterPage(AeroWizard.WizardPage page)
		{
			_page = page;

			_page.Initialize += (s, e) =>
				{
					if (this.Initialize != null)
						this.Initialize(s, e);
				};
		}

		public object Tag
		{
			get
			{
				return _page.Tag;
			}
			set
			{
				_page.Tag = value;
			}
		}

		public bool Suppress
		{
			get
			{
				return _page.Suppress;
			}
			set
			{
				_page.Suppress = value;
			}
		}

		public bool AllowNext
		{
			get
			{
				return _page.AllowNext;
			}
			set
			{
				_page.AllowNext = value;
			}
		}

		public WizardAdapterPage NextPage
		{
			get
			{
				return new WizardAdapterPage(_page.NextPage);
			}
			set
			{
				_page.NextPage = value._page;
			}
		}

		public bool IsFinishPage
		{
			get
			{
				return _page.IsFinishPage;
			}
			set
			{
				_page.IsFinishPage = value;
			}
		}

		#region Control overrides

		public override string Text
		{
			get
			{
				return _page.Text;

			}
			set
			{
				_page.Text = value;
			}
		}

		#endregion

		public event System.EventHandler<AeroWizard.WizardPageInitEventArgs> Initialize;
	}


}
