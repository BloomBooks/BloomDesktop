using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;

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

        public List<WizardPage> Pages
        {
            get;
            protected set;
        }

        public string Title
        {
            get;
            set;
        }

		public string NextButtonText 
		{
			get;
			set;
		}

		public string FinishButtonText
		{
			get;
			set;
		}

        public Icon TitleIcon
        {
            get;
            set;
        }

        public void BeginInit()
        {
			_backButton = new Button { Text = "Back", Size = new Size(75, 25), Left = 0};
			_nextAndFinishedButton = new Button { Text = "Next", Size = new Size(75, 25), Left = 80};
			_cancelButton = new Button { Text = "Cancel", Size = new Size(75, 25), Left = 160 };

			_contentPanel = new Panel { Dock = DockStyle.Fill };

			_buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 35, Padding = new Padding(5) };
			var panel = new Panel { Dock = DockStyle.Right, AutoSize = true };
			panel.Controls.Add(_backButton);
			panel.Controls.Add(_nextAndFinishedButton);
			panel.Controls.Add(_cancelButton);

			_buttonPanel.Controls.Add(panel);
        }

        public void EndInit()
        {
            ShowPage(0);

            _nextAndFinishedButton.Click += nextAndFinishedButton_Click;
            _backButton.Click += _backButton_Click;
            _cancelButton.Click += _cancelButton_Click;

			Controls.Add(_contentPanel);
			Controls.Add(_buttonPanel);
        }

        void _cancelButton_Click(object sender, EventArgs e)
        {
            if (Cancelled != null)
                Cancelled(this, EventArgs.Empty);
        }

        void _backButton_Click(object sender, EventArgs e)
        {
            ShowPage(--_currentPageIndex);

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

            ShowPage(++_currentPageIndex);
            InvokePagedChangedEvent();           
        }

        protected void InvokePagedChangedEvent()
        {
            if (_currentShownPage.IsFinishPage)
            {
                _nextAndFinishedButton.Text = "Finished";
            }
            else
            {
                _nextAndFinishedButton.Text = "Next";
            }

            if (SelectedPageChanged != null)
                SelectedPageChanged(this, EventArgs.Empty);
        }

        protected virtual void ShowPage(int pageNumber)
        {
			if (_currentShownPage != null)
				_contentPanel.Controls.Remove(_currentShownPage);

            _currentPageIndex = pageNumber;
            _currentShownPage = Pages[pageNumber];
            _currentShownPage.InvokeInitializeEvent();
			_contentPanel.Controls.Add(_currentShownPage);
            _currentShownPage.Dock = DockStyle.Fill;
            _backButton.Enabled = pageNumber != 0;
            _nextAndFinishedButton.Enabled = _currentShownPage.AllowNext;
            _currentShownPage.AllowNextChanged -= _currentShownPage_AllowNextChanged;
            _currentShownPage.AllowNextChanged += _currentShownPage_AllowNextChanged;
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
