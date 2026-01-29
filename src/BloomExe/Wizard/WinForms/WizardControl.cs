using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using L10NSharp;

namespace Bloom.Wizard.WinForms
{
    class WizardControl : Control
    {
        public event EventHandler Cancelled;

        public event EventHandler Finished;

        public event EventHandler SelectedPageChanged;

        #region protected member vars

        Panel _contentPanel;
        Panel _buttonPanel;
        Button _nextAndFinishedButton;
        Button _backButton;
        Button _cancelButton;
        int _currentPageIndex;
        WizardPage _currentShownPage;
        private readonly Stack<int> _history = new Stack<int>();

        #endregion

        public WizardControl()
        {
            Dock = DockStyle.Fill;

            Pages = new List<WizardPage>();
        }

        public WizardPage SelectedPage
        {
            get
            {
                if (Pages.Count <= 0)
                    return null;

                return _currentShownPage;
            }
        }

        public List<WizardPage> Pages { get; protected set; }

        public string Title { get; set; }

        public string NextButtonText { get; set; }

        public string FinishButtonText { get; set; }

        public string CancelButtonText
        {
            get { return _cancelButton.Text; }
            set { _cancelButton.Text = value; }
        }

        public Icon TitleIcon { get; set; }

        public void BeginInit()
        {
            _backButton = new Button
            {
                Text = "Back",
                Size = new Size(75, 25),
                Left = 0,
            };
            _backButton.Text = LocalizationManager.GetString(
                "Common.BackButton",
                "Back",
                "In a wizard, this button takes you to the previous step."
            );
            _nextAndFinishedButton = new Button
            {
                Text = "Next",
                Size = new Size(75, 25),
                Left = 80,
            };
            _nextAndFinishedButton.Text = LocalizationManager.GetString(
                "Common.NextButton",
                "Next",
                "In a wizard, this button takes you to the next step."
            );
            _cancelButton = new Button
            {
                Text = "Cancel",
                Size = new Size(75, 25),
                Left = 160,
            };
            _cancelButton.Text = LocalizationManager.GetString("Common.CancelButton", "Cancel");

            _contentPanel = new Panel { Dock = DockStyle.Fill };

            _buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                Padding = new Padding(5),
            };
            var panel = new Panel { Dock = DockStyle.Right, AutoSize = true };
            panel.Controls.Add(_backButton);
            panel.Controls.Add(_nextAndFinishedButton);
            panel.Controls.Add(_cancelButton);

            _buttonPanel.Controls.Add(panel);
        }

        public void EndInit()
        {
            _nextAndFinishedButton.Click += nextAndFinishedButton_Click;
            _backButton.Click += _backButton_Click;
            _cancelButton.Click += _cancelButton_Click;

            Controls.Add(_contentPanel);
            Controls.Add(_buttonPanel);
        }

        public void ShowFirstPage()
        {
            ShowPage(0);
        }

        void _cancelButton_Click(object sender, EventArgs e)
        {
            if (Cancelled != null)
                Cancelled(this, EventArgs.Empty);
        }

        void _backButton_Click(object sender, EventArgs e)
        {
            if (_history.Count < 2)
                return;

            _history.Pop();
            ShowPage(_history.Pop());

            InvokePagedChangedEvent();
        }

        void nextAndFinishedButton_Click(object sender, EventArgs e)
        {
            if (_currentShownPage.IsFinishPage)
            {
                if (Finished != null)
                    Finished(this, EventArgs.Empty);

                return;
            }

            // Prevent navigation if AllowNext is false (e.g., from double-clicking)
            if (!_currentShownPage.AllowNext)
            {
                return;
            }

            ShowPage(GetNextPage());
            InvokePagedChangedEvent();
        }

        protected void InvokePagedChangedEvent()
        {
            UpdateNextAndFinishedButtonText();

            if (SelectedPageChanged != null)
                SelectedPageChanged(this, EventArgs.Empty);
        }

        public void UpdateNextAndFinishedButtonText()
        {
            if (_nextAndFinishedButton != null && _currentShownPage != null)
                _nextAndFinishedButton.Text = _currentShownPage.IsFinishPage
                    ? FinishButtonText
                    : NextButtonText;
        }

        protected virtual void ShowPage(int pageNumber)
        {
            ShowPage(Pages[pageNumber], pageNumber);
        }

        protected virtual void ShowPage(WizardPage page)
        {
            ShowPage(page, Pages.IndexOf(page));
        }

        private void ShowPage(WizardPage page, int pageNumber)
        {
            if (page.Suppress)
            {
                ShowPage(GetNextPage());
                return;
            }
            if (_currentShownPage != null)
                _contentPanel.Controls.Remove(_currentShownPage);

            _currentPageIndex = pageNumber;
            _currentShownPage = page;
            _currentShownPage.InvokeInitializeEvent();
            _currentShownPage.Dock = DockStyle.Fill;

            _currentShownPage.BeforeShow();

            _contentPanel.Controls.Add(_currentShownPage);
            _backButton.Enabled = _history.Count > 0;
            _nextAndFinishedButton.Enabled = _currentShownPage.AllowNext;
            _currentShownPage.AllowNextChanged -= _currentShownPage_AllowNextChanged;
            _currentShownPage.AllowNextChanged += _currentShownPage_AllowNextChanged;

            _history.Push(pageNumber);
        }

        private WizardPage GetNextPage()
        {
            if (_currentShownPage != null && _currentShownPage.NextPage != null)
                return _currentShownPage.NextPage;
            return Pages[++_currentPageIndex];
        }

        void _currentShownPage_AllowNextChanged(object sender, EventArgs e)
        {
            _nextAndFinishedButton.Enabled = _currentShownPage.AllowNext;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);

            var form = FindForm();
            if (form == null)
                return;

            form.Text = Title;
            form.Icon = TitleIcon;
        }
    }
}
