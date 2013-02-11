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

		bool _useAeroWizard = true;

		#region Implemetaion specific logic
		public void Setup()
		{
			if (_useAeroWizard)
			{
				InitializeControl = () =>
				{
					_aeroWizard = new MyFixedAeroWizard();

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
				};

				GetSelectedPage = () => new WizardAdapterPage(_aeroWizard.SelectedPage);

				GetPages = () =>
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
				};

				GetTitle = () => _aeroWizard.Title;
				SetTitle = (value) => _aeroWizard.Title = value;
				GetIcon = () => _aeroWizard.TitleIcon;
				SetIcon = (icon) => _aeroWizard.TitleIcon = icon;

				BeginInitLogic = () => _aeroWizard.BeginInit();
				EndInitLogic = () =>
					{
						foreach (WizardAdapterPage page in _pages)
						{
							page.AeroPage.Controls.AddRange(page.Controls.Cast<Control>().ToArray());

							_aeroWizard.Pages.Add(page.AeroPage);

						}

						this.Controls.Add(_aeroWizard);

						_aeroWizard.EndInit();
					};

			}
			else
			{

			}
		}

		Action InitializeControl;
		Func<WizardAdapterPage> GetSelectedPage;
		Func<List<WizardAdapterPage>> GetPages;
		Func<string> GetTitle;
		Action<string> SetTitle;
		Func<Icon> GetIcon;
		Action<Icon> SetIcon;
		Action BeginInitLogic;
		Action EndInitLogic;

		#endregion

		public WizardAdapterControl()
		{
			Setup();

			this.Dock = DockStyle.Fill;

			InitializeControl();
		}

		public WizardAdapterPage SelectedPage
		{
			get { return GetSelectedPage(); }
		}

		List<WizardAdapterPage> _pages;

		public List<WizardAdapterPage> Pages {
			get
			{
				return GetPages();
			}
		}

		public string Title
		{
			get
			{
				return GetTitle();
			}
			set
			{
				SetTitle(value);
			}
		}

		public Icon TitleIcon
		{
			get
			{
				return GetIcon();
			}
			set
			{
				SetIcon(value);
			}
		}

		public event EventHandler Cancelled;

		public event EventHandler Finished;

		public event EventHandler SelectedPageChanged;

		#region ISupportInitialize implementation
		void ISupportInitialize.BeginInit()
		{
			BeginInitLogic();
		}

		void ISupportInitialize.EndInit()
		{
			EndInitLogic();
		}
		#endregion
	}

	class WizardAdapterPage : Control
	{
		bool _useAeroWizard = true;

		AeroWizard.WizardPage _page;

		#region Implemetaion specific logic
		public void Setup()
		{
			if (_useAeroWizard)
			{
				InitializeControl = (page) =>
				{
					_page = (AeroWizard.WizardPage)page;

					_page.Initialize += (s, e) =>
					{
						if (this.Initialize != null)
							this.Initialize(s, e);
					};

					GetTag = () =>  _page.Tag;
					SetTag = (value) => _page.Tag = value;
					GetSuppress = () => _page.Suppress;
					SetSuppress = (value) => _page.Suppress = value;
					GetAllowNext = () => _page.AllowNext;
					SetAllowNext = (value) => _page.AllowNext = value;
					GetNextPage = () => new WizardAdapterPage(_page.NextPage);
					SetNextPage = (value) => _page.NextPage = value._page;
					GetIsFinishedPage = () => _page.IsFinishPage;
					SetIsFinishedPage = (value) => _page.IsFinishPage = value;
					GetText = () => _page.Text;
					SetText = (value) => _page.Text = value;
				};
			}
			else
			{

			}
		}


		internal AeroWizard.WizardPage AeroPage
		{
			get
			{
				return _page;
			}
		}

		Action<object> InitializeControl;
		Func<object> GetTag;
		Action<object> SetTag;
		Func<bool> GetSuppress;
		Action<bool> SetSuppress;
		Func<bool> GetAllowNext;
		Action<bool> SetAllowNext;
		Func<WizardAdapterPage> GetNextPage;
		Action<WizardAdapterPage> SetNextPage;
		Func<bool> GetIsFinishedPage;
		Action<bool> SetIsFinishedPage;
		Func<string> GetText;
		Action<string> SetText;


		#endregion

		public WizardAdapterPage() : this(new AeroWizard.WizardPage())
		{

		}

		internal WizardAdapterPage(AeroWizard.WizardPage page)
		{
			Setup();

			InitializeControl(page);
		}

		public new object Tag
		{
			get
			{
				return GetTag();
			}
			set
			{
				SetTag(value);
			}
		}

		public bool Suppress
		{
			get
			{
				return GetSuppress();
			}
			set
			{
				SetSuppress(value);
			}
		}

		public bool AllowNext
		{
			get
			{
				return GetAllowNext();
			}
			set
			{
				SetAllowNext(value);
			}
		}

		public WizardAdapterPage NextPage
		{
			get
			{
				return GetNextPage();
			}
			set
			{
				SetNextPage(value);
			}
		}

		public bool IsFinishPage
		{
			get
			{
				return GetIsFinishedPage();
			}
			set
			{
				SetIsFinishedPage(value);
			}
		}

		#region Control overrides

		public override string Text
		{
			get
			{
				return GetText();

			}
			set
			{
				SetText(value);
			}
		}

		public new Size Size
		{
			get
			{
				return _page.Size;
			}
			set
			{
				_page.Size = value;
			}
		}




		#endregion

		public event System.EventHandler<AeroWizard.WizardPageInitEventArgs> Initialize;
	}


}
