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

		protected WinForms.WizardControl _winformsWizard;

		internal static bool _useAeroWizard = Platform.Utilities.Platform.IsWindows && (System.Environment.GetEnvironmentVariable("USE_WINFORM_WIZARD") == null);

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
				GetNextButtonText = () => _aeroWizard.NextButtonText;
				SetNextButtonText = (value) => _aeroWizard.NextButtonText = value;
				GetFinishButtonText= () => _aeroWizard.FinishButtonText;
				SetFinishButtonText= (value) => _aeroWizard.FinishButtonText = value;
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
				InitializeControl = () =>
				{
					_winformsWizard = new WinForms.WizardControl();

					_winformsWizard.Cancelled += (sender, e) =>
					{
						if (this.Cancelled != null)
							this.Cancelled(sender, e);
					};

					_winformsWizard.Finished += (sender, e) =>
					{
						if (this.Finished != null)
							this.Finished(sender, e);
					};

					_winformsWizard.SelectedPageChanged += (sender, e) =>
					{
						if (this.SelectedPageChanged != null)
							this.SelectedPageChanged(sender, e);
					};
				};

				GetSelectedPage = () => new WizardAdapterPage(_winformsWizard.SelectedPage);

				GetPages = () =>
				{
					if (_pages == null)
					{
						_pages = new List<WizardAdapterPage>();
						foreach (var page in _winformsWizard.Pages)
						{
							_pages.Add(new WizardAdapterPage(page));
						}
					}

					return _pages;
				};

				GetTitle = () => _winformsWizard.Title;
				SetTitle = (value) => _winformsWizard.Title = value;
				GetNextButtonText = () => _winformsWizard.NextButtonText;
				SetNextButtonText = (value) => _winformsWizard.NextButtonText = value;
				GetFinishButtonText= () => _winformsWizard.FinishButtonText;
				SetFinishButtonText= (value) => _winformsWizard.FinishButtonText = value;
				GetIcon = () => _winformsWizard.TitleIcon;
				SetIcon = (icon) => _winformsWizard.TitleIcon = icon;

				BeginInitLogic = () => _winformsWizard.BeginInit();
				EndInitLogic = () =>
				{
					foreach (WizardAdapterPage page in _pages)
					{
						page.WinFormPage.Controls.AddRange(page.Controls.Cast<Control>().ToArray());

						_winformsWizard.Pages.Add(page.WinFormPage);
					}

					this.Controls.Add(_winformsWizard);

					_winformsWizard.EndInit();
				};
			}
		}

		Action InitializeControl;
		Func<WizardAdapterPage> GetSelectedPage;
		Func<List<WizardAdapterPage>> GetPages;
		Func<string> GetTitle;
		Action<string> SetTitle;
		Func<string> GetNextButtonText;
		Action<string> SetNextButtonText;
		Func<string> GetFinishButtonText;
		Action<string> SetFinishButtonText;
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

		public string NextButtonText
		{
			get
			{
				return GetNextButtonText();
			}
			set
			{
				SetNextButtonText(value);
			}
		}
		public string FinishButtonText
		{
			get
			{
				return GetFinishButtonText();
			}
			set
			{
				SetFinishButtonText(value);
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
		AeroWizard.WizardPage _aeroPage;

		WinForms.WizardPage _winformPage;

		#region Implemetaion specific logic
		public void Setup()
		{
			if (WizardAdapterControl._useAeroWizard)
			{
				InitializeControl = (page) =>
				{
					_aeroPage = (AeroWizard.WizardPage)page;

					_aeroPage.Initialize += (s, e) =>
					{
						if (this.Initialize != null)
							this.Initialize(s, e);
					};

					GetTag = () =>  _aeroPage.Tag;
					SetTag = (value) => _aeroPage.Tag = value;
					GetSuppress = () => _aeroPage.Suppress;
					SetSuppress = (value) => _aeroPage.Suppress = value;
					GetAllowNext = () => _aeroPage.AllowNext;
					SetAllowNext = (value) => _aeroPage.AllowNext = value;
					GetNextPage = () => new WizardAdapterPage(_aeroPage.NextPage);
					SetNextPage = (value) => _aeroPage.NextPage = value._aeroPage;
					GetIsFinishedPage = () => _aeroPage.IsFinishPage;
					SetIsFinishedPage = (value) => _aeroPage.IsFinishPage = value;
					GetText = () => _aeroPage.Text;
					SetText = (value) => _aeroPage.Text = value;
					GetSize = () => _aeroPage.Size;
					SetSize = (value) => _aeroPage.Size = value;

				};
			}
			else
			{
				InitializeControl = (page) =>
				{
					_winformPage = (WinForms.WizardPage)page;

					_winformPage.Initialize += (s, e) =>
					{
						if (this.Initialize != null)
							this.Initialize(s, e);
					};

					GetTag = () => _winformPage.Tag;
					SetTag = (value) => _winformPage.Tag = value;
					GetSuppress = () => _winformPage.Suppress;
					SetSuppress = (value) => _winformPage.Suppress = value;
					GetAllowNext = () => _winformPage.AllowNext;
					SetAllowNext = (value) => _winformPage.AllowNext = value;
					GetNextPage = () => new WizardAdapterPage(_winformPage.NextPage);
					SetNextPage = (value) => _winformPage.NextPage = value._winformPage;
					GetIsFinishedPage = () => _winformPage.IsFinishPage;
					SetIsFinishedPage = (value) => _winformPage.IsFinishPage = value;
					GetText = () => _winformPage.Text;
					SetText = (value) => _winformPage.Text = value;
					GetSize = () => _winformPage.Size;
					SetSize = (value) => _winformPage.Size = value;
				};
			}
		}


		internal AeroWizard.WizardPage AeroPage
		{
			get
			{
				return _aeroPage;
			}
		}

		internal WinForms.WizardPage WinFormPage
		{
			get
			{
				return _winformPage;
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
		Func<Size> GetSize;
		Action<Size> SetSize;


		#endregion

		public WizardAdapterPage()
			: this(WizardAdapterControl._useAeroWizard ? (Control)new AeroWizard.WizardPage() : (Control)new WinForms.WizardPage())
		{

		}

		protected WizardAdapterPage(Control page)
		{
			Setup();

			InitializeControl(page);
		}

		internal WizardAdapterPage(AeroWizard.WizardPage page)
		{
			Setup();

			InitializeControl(page);
		}

		internal WizardAdapterPage(WinForms.WizardPage page)
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
				return GetSize();
			}
			set
			{
				SetSize(value);
			}
		}




		#endregion

		public event System.EventHandler<EventArgs> Initialize;
	}


}
