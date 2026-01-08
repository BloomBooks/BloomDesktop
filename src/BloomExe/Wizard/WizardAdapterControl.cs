using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Bloom.Wizard
{
    /// <summary>
    /// Not sure if this control is even needed now. We used to have two different Wizard implementations,
    /// but now we're only using the WinForms version.
    /// </summary>
    class WizardAdapterControl : Control, ISupportInitialize
    {
        protected WinForms.WizardControl _winformsWizard;

        #region Implementation specific logic
        public void Setup()
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
            GetFinishButtonText = () => _winformsWizard.FinishButtonText;
            SetFinishButtonText = (value) => _winformsWizard.FinishButtonText = value;
            GetCancelButtonText = () => _winformsWizard.CancelButtonText;
            SetCancelButtonText = (value) => _winformsWizard.CancelButtonText = value;
            GetIcon = () => _winformsWizard.TitleIcon;
            SetIcon = (icon) => _winformsWizard.TitleIcon = icon;

            BeginInitLogic = () => _winformsWizard.BeginInit();
            EndInitLogic = () =>
            {
                foreach (WizardAdapterPage page in _pages)
                {
                    page.WinFormPage.AddControls(page.Controls.Cast<Control>().ToArray());

                    _winformsWizard.Pages.Add(page.WinFormPage);
                }

                this.Controls.Add(_winformsWizard);

                _winformsWizard.EndInit();
            };
            AfterInitialization = () => _winformsWizard.ShowFirstPage();
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
        Func<string> GetCancelButtonText;
        Action<string> SetCancelButtonText;
        Func<Icon> GetIcon;
        Action<Icon> SetIcon;
        Action BeginInitLogic;
        Action EndInitLogic;
        internal Action AfterInitialization;

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

        public List<WizardAdapterPage> Pages
        {
            get { return GetPages(); }
        }

        public string Title
        {
            get { return GetTitle(); }
            set { SetTitle(value); }
        }

        public string NextButtonText
        {
            get { return GetNextButtonText(); }
            set { SetNextButtonText(value); }
        }
        public string FinishButtonText
        {
            get { return GetFinishButtonText(); }
            set { SetFinishButtonText(value); }
        }
        public string CancelButtonText
        {
            get { return GetCancelButtonText(); }
            set { SetCancelButtonText(value); }
        }

        public Icon TitleIcon
        {
            get { return GetIcon(); }
            set { SetIcon(value); }
        }

        public event EventHandler Cancelled;

        public event EventHandler Finished;

        public event EventHandler SelectedPageChanged;

        public void UpdateNextAndFinishButtonText()
        {
            _winformsWizard.UpdateNextAndFinishedButtonText();
        }

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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var wizardAdapterPage in Pages)
                {
                    wizardAdapterPage.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    class WizardAdapterPage : Control
    {
        WinForms.WizardPage _winformPage;

        #region Implementation specific logic
        public void Setup()
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

        internal WinForms.WizardPage WinFormPage
        {
            get { return _winformPage; }
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
            : this((Control)new WinForms.WizardPage()) { }

        protected internal WizardAdapterPage(Control page)
        {
            Setup();

            InitializeControl(page);
        }

        public new object Tag
        {
            get { return GetTag != null ? GetTag() : null; }
            set
            {
                if (SetTag != null)
                    SetTag(value);
            }
        }

        public bool Suppress
        {
            get { return GetSuppress != null && GetSuppress(); }
            set
            {
                if (SetSuppress != null)
                    SetSuppress(value);
            }
        }

        public bool AllowNext
        {
            get { return GetAllowNext != null && GetAllowNext(); }
            set
            {
                if (SetAllowNext != null)
                    SetAllowNext(value);
            }
        }

        public WizardAdapterPage NextPage
        {
            get { return GetNextPage != null ? GetNextPage() : null; }
            set
            {
                if (SetNextPage != null)
                    SetNextPage(value);
            }
        }

        public bool IsFinishPage
        {
            get { return GetIsFinishedPage != null && GetIsFinishedPage(); }
            set
            {
                if (SetIsFinishedPage != null)
                    SetIsFinishedPage(value);
            }
        }

        #region Control overrides

        public override string Text
        {
            get { return GetText != null ? GetText() : null; }
            set
            {
                if (SetText != null)
                    SetText(value);
            }
        }

        public new Size Size
        {
            get { return GetSize != null ? GetSize() : Size.Empty; }
            set
            {
                if (SetSize != null)
                    SetSize(value);
            }
        }
        #endregion

        public event EventHandler<EventArgs> Initialize;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _winformPage.Dispose();
            base.Dispose(disposing);
        }
    }
}
