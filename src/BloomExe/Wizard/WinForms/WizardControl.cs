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

		TableLayoutPanel _tableLayoutPanel;
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

		public Icon TitleIcon
		{
			get;
			set;
		}

		public void BeginInit()
		{
			const int SeperatorSize = 10;
			_backButton = new Button { Text = "Back", Size = new Size(100, 25)};
			_nextAndFinishedButton = new Button { Text = "Next", Size = new Size(100, 25)};
			_cancelButton = new Button { Text = "Cancel", Size = new Size(100, 25)};
			_tableLayoutPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
			_tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
			_tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			_tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

			var buttonTableLayoutPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 4 };
			buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
			buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
			buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

			buttonTableLayoutPanel.Controls.Add(_backButton, 1, 0);
			buttonTableLayoutPanel.Controls.Add(_nextAndFinishedButton, 2, 0);
			buttonTableLayoutPanel.Controls.Add(_cancelButton, 3, 0);

			_tableLayoutPanel.Controls.Add(buttonTableLayoutPanel, 0, 2);
		}

		public void EndInit()
		{
			// TODO: draw TitleIcon somewhere.
			_tableLayoutPanel.Controls.Add(new Label { Text = Title, AutoSize = true, Font = new Font(new FontFamily("Arial"),14) }, 0, 0);

			ShowPage(0);

			_nextAndFinishedButton.Click += nextAndFinishedButton_Click;
			_backButton.Click += _backButton_Click;
			_cancelButton.Click += _cancelButton_Click;

		   this.Controls.Add(_tableLayoutPanel);
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			_tableLayoutPanel.Width = this.Width;

			base.OnSizeChanged(e);
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
				_tableLayoutPanel.Controls.Remove(_currentShownPage);

			_currentPageIndex = pageNumber;
			_currentShownPage = Pages[pageNumber];
			_currentShownPage.InvokeInitializeEvent();
			_tableLayoutPanel.Controls.Add(_currentShownPage, 0, 1);
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
	}
}
