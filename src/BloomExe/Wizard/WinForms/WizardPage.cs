using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Bloom.CollectionCreating;
using Bloom.Workspace;

namespace Bloom.Wizard.WinForms
{
    class WizardPage : Control
    {
        private static ToolStripDropDownButton s_toolStripDropDownButton;

        public event EventHandler<WizardPageInitEventArgs> Initialize;

        internal event EventHandler<EventArgs> AllowNextChanged;

        bool _allowNext;
        private WizardPage _nextPage;
        private readonly Label _titleLabel;
        private readonly Panel _pagePanel;
        private ToolStrip _toolStrip;

        public WizardPage()
        {
            if (s_toolStripDropDownButton == null || s_toolStripDropDownButton.IsDisposed)
                CreateUiLanguageMenuButton();

            AllowNext = true;
            BackColor = Color.White;

            var titlePanel = new TableLayoutPanel
            {
                Padding = new Padding(30, 30, 30, 0),
                AutoSize = true,
                Dock = DockStyle.Top,
            };
            _titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font(new FontFamily("Arial"), 12),
                Dock = DockStyle.Top,
                ForeColor = Color.DarkBlue,
                Padding = new Padding(0, 3, 0, 0),
            };

            titlePanel.ColumnCount = 2;
            titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titlePanel.Controls.Add(_titleLabel, 0, 0);
            titlePanel.Controls.Add(GetUiLanguageMenuToolStrip(), 1, 0);
            titlePanel.RowCount = 1;
            titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _pagePanel = new Panel
            {
                Padding = new Padding(30),
                AutoSize = true,
                Dock = DockStyle.Fill,
            };
            Controls.Add(_pagePanel);
            Controls.Add(titlePanel);
        }

        public bool Suppress { get; set; }

        public bool AllowNext
        {
            get { return _allowNext; }
            set
            {
                _allowNext = value;
                if (AllowNextChanged != null)
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
            internal set { _nextPage = value; }
        }

        public bool IsFinishPage { get; set; }

        internal void BeforeShow()
        {
            // This awkwardly reuses the same ToolStripDropDownButton by moving it from page to page.
            // The alternative was awkwardly subscribing each instance to every other instance's change event.
            var currentParent = s_toolStripDropDownButton.GetCurrentParent();
            if (currentParent != null)
                currentParent.Items.Remove(s_toolStripDropDownButton);
            _toolStrip.Items.Add(s_toolStripDropDownButton);
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
                _titleLabel.Text = value;
            }
        }

        public void AddControls(Control[] controls)
        {
            if (controls.Length == 1)
            {
                controls[0].AutoSize = true;
                controls[0].Dock = DockStyle.Fill;
            }
            _pagePanel.Controls.AddRange(controls);
        }

        private ToolStrip GetUiLanguageMenuToolStrip()
        {
            _toolStrip = new ToolStrip
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = _titleLabel.Font,
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new NoBorderToolStripRenderer(),
            };
            return _toolStrip;
        }

        private static void CreateUiLanguageMenuButton()
        {
            s_toolStripDropDownButton = new ToolStripDropDownButton
            {
                AutoSize = true,
                Alignment = ToolStripItemAlignment.Right,
                Text = "English",
                // Shares the tooltip string with OpenCreateNewCollectionsDialog.UILanguageMenu
                ToolTipText = L10NSharp.LocalizationManager.GetString(
                    "OpenCreateNewCollectionsDialog.UILanguageMenu_ToolTip_",
                    "Change user interface language"
                ),
            };
            WorkspaceView.SetupUiLanguageMenuCommon(
                s_toolStripDropDownButton,
                FinishUiLanguageMenuItemClick
            );
        }

        private static void FinishUiLanguageMenuItemClick()
        {
            ToolStripItem selectedItem = null;
            foreach (ToolStripItem dropDownItem in s_toolStripDropDownButton.DropDownItems)
            {
                if (dropDownItem.Selected)
                {
                    selectedItem = dropDownItem;
                    break;
                }
            }
            if (selectedItem != null)
            {
                var newCollectionWizard = GetNewCollectionWizard(
                    s_toolStripDropDownButton.GetCurrentParent()
                );
                if (newCollectionWizard != null)
                    newCollectionWizard.ChangeLocalization();
            }
        }

        private static NewCollectionWizard GetNewCollectionWizard(Control ctrl)
        {
            while (true)
            {
                var parent = ctrl.Parent;

                if (parent == null)
                    return null;

                if (parent.GetType() == typeof(NewCollectionWizard))
                    return (NewCollectionWizard)parent;

                ctrl = parent;
            }
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
    class WizardPageInitEventArgs : EventArgs { }
}
