using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using L10NSharp;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ClearShare.WinFormsUI;

namespace Bloom.Edit
{
    public class BloomMetadataEditorDialog : MetadataEditorDialog
    {
        public CheckBox _replaceCopyrightCheckbox;

        public BloomMetadataEditorDialog(Metadata metadata, bool isDerivedBook)
            : base(metadata)
        {
            ShowCreator = false;
            if (isDerivedBook)
            {
                this.SuspendLayout();

                _replaceCopyrightCheckbox = new CheckBox();
                _replaceCopyrightCheckbox.Text = LocalizationManager.GetString(
                    "EditTab.CopyrightThisDerivedVersion",
                    "Copyright and license this translated version"
                );
                _replaceCopyrightCheckbox.Checked = true;
                _replaceCopyrightCheckbox.CheckedChanged += ReplaceCopyrightCheckboxChanged;
                _replaceCopyrightCheckbox.Location = new System.Drawing.Point(20, 10);
                _replaceCopyrightCheckbox.AutoSize = true;

                var topPanel = new Panel();
                topPanel.SuspendLayout();
                topPanel.Dock = System.Windows.Forms.DockStyle.Top;
                topPanel.AutoSize = true;
                topPanel.Controls.Add(_replaceCopyrightCheckbox);

                this.Controls.Add(topPanel);
                // I don't know why we need to subtract 30 here but it avoids an undesirably larger gap above
                // the OK and Cancel buttons.
                this.Size = new System.Drawing.Size(this.Width, this.Height + topPanel.Height - 30);
                MetadataControl.BringToFront();
                topPanel.ResumeLayout(false);
                topPanel.PerformLayout();
                this.ResumeLayout(false);
            }

            // The tweaks here allow us to adjust the layout of the dialog so it fits better on smaller screens.
            // For a long-term solution it would be better to fix the underlying implementation in LibPalaso.
            // But this fix has a very short lifetime; it is already replaced with a new React implementation
            // in 5.3. So this is easier than negotiating the redesign of a possibly shared dialog.

            // The spec for BL-11207 says we can reduce the copyright holder field to one line, but this
            // actually still has room for at least two, yet still gives us a good reduction. Also, we need the remaining
            // height for some localizations (e.g., az) of "Copyright holder")
            const int copyrightPanelReduction = 30;
            // The actual copyright holder field is docked in such a way that all we have to do is shrink the
            // containing panel (and move the lower controls up).
            var copyrightPanel = MetadataControl
                .Controls.Cast<Control>()
                .First(x => x is TableLayoutPanel);
            copyrightPanel.Height -= copyrightPanelReduction;
            foreach (Control c in MetadataControl.Controls)
            {
                if (c.Top > copyrightPanel.Bottom)
                    c.Top -= copyrightPanelReduction;
            }

            // We will also place the CC license picture over on the right, saving its height plus a little padding.
            var ccLicenseBox = MetadataControl
                .Controls.Cast<Control>()
                .First(x => x is BetterPictureBox);
            int ccLicenseBoxReduction = ccLicenseBox.Height + 5;
            foreach (Control c in MetadataControl.Controls)
            {
                if (c.Top > ccLicenseBox.Bottom)
                    c.Top -= ccLicenseBoxReduction;
            }
            ccLicenseBox.Top -= 165;
            // What we want to do here is add 250 to its Left to move it over. We can't do that because
            // the Libpalaso code uses the Left of this control to align the Left of the LicenseRights
            // control when the CC image is hidden (i.e., when it contains Custom rights rather than
            // Additional Requests). So, instead, we make it wider by twice that amount.
            // Centering of the image puts it where we want.
            ccLicenseBox.Width += 500;

            // Even before these tweaks, this control sometimes lost part of its last letter to the label on its right.
            var cc0Box = MetadataControl
                .Controls.Cast<Control>()
                .First(x => x.Name == "_publicDomainCC0");
            cc0Box.BringToFront();

            // panel2 contains the "Allow commercial use..." heading and radio buttons.
            // The heading may go the full width in some localizations, so we've arranged to place the image
            // to the right of the "yes/no" radio buttons, which are unlikely to need to be too wide.
            // But we can't just make the panel narrower; bad things happen if it needs to wrap the heading
            // onto a second line. Nor does Windows.Forms support giving it a transparent background.
            // So the only way to allow its first child to be wide and the picture to be seen below that
            // and right of the radio buttons is to move the heading out of the panel, adjust the positions
            // of the panel children, and make the panel less wide so it doesn't cover the picture.
            // (We can't just promote ALL the children and get rid of the panel, because the behavior
            // of the radio buttons is somehow tied to being grouped in this panel.)
            var panel2 = MetadataControl.Controls.Cast<Control>().First(x => x.Name == "panel2");
            var heading = panel2.Controls[0];
            heading.Location = new Point(
                heading.Location.X + panel2.Location.X,
                heading.Location.Y + panel2.Location.Y
            );
            MetadataControl.Controls.Add(heading);
            const int headingHeight = 17;
            foreach (Control c in panel2.Controls.Cast<Control>().ToArray())
            {
                c.Top -= headingHeight;
            }
            panel2.Height -= headingHeight;
            panel2.Top += headingHeight;
            panel2.Width = 250;

            // This is just a tweak. It wasn't well aligned before, but now it's between two other boxes
            // and closer to one of them, it is more noticeable, so we adjust it.
            var moreInfoBox = MetadataControl
                .Controls.Cast<Control>()
                .First(x => x.Name == "_linkToDefinitionOfNonCommercial");
            moreInfoBox.Left -= 5;

            // This is how much we get to shrink the main control and the whole dialog.
            var totalReduction = copyrightPanelReduction + ccLicenseBoxReduction;
            MetadataControl.Height -= totalReduction;
            Height -= totalReduction; // OK and Cancel buttons move by being docked.
        }

        private void ReplaceCopyrightCheckboxChanged(Object sender, EventArgs e)
        {
            MetadataControl.Enabled = _replaceCopyrightCheckbox.Checked;
        }

        protected override void _minimallyCompleteCheckTimer_Tick(object sender, EventArgs e)
        {
            if (_replaceCopyrightCheckbox == null || _replaceCopyrightCheckbox.Checked)
                base._minimallyCompleteCheckTimer_Tick(sender, e);
            else
                OkButton.Enabled = true;
        }

        public bool ReplaceOriginalCopyright
        {
            get
            {
                return (_replaceCopyrightCheckbox != null)
                    ? _replaceCopyrightCheckbox.Checked
                    : true;
            }
            set
            {
                if (_replaceCopyrightCheckbox != null)
                {
                    _replaceCopyrightCheckbox.Checked = value;
                    MetadataControl.Enabled = value;
                }
            }
        }
    }
}
