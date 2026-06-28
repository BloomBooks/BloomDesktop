using System;
using System.Windows.Forms;

namespace Bloom
{
    /// <summary>
    /// Make these 3 calls: CreateAndShow(), StayAboveThisWindow(), FadeAndClose()
    ///
    /// </summary>
    public partial class SplashScreen : Form
    {
        public static SplashScreen CreateAndShow()
        {
            var f = new SplashScreen();
            f.Show();
            Application.DoEvents(); //show the splash
            return f;
        }

        public void StayAboveThisWindow(Form window)
        {
            TopMost = true;
            Owner = window; //required to keep it on top
        }

        public void FadeAndClose()
        {
            Invoke((Action)(() => _fadeOutTimer.Enabled = true));
        }

        private SplashScreen()
        {
            InitializeComponent();
            _shortVersionLabel.Text = Shell.GetShortVersionInfo();
            _longVersionInfo.Text = "";
            _feedbackStatusLabel.Visible = !DesktopAnalytics.Analytics.AllowTracking;
            // Keep the bottom-edge controls correctly placed ourselves; see LayoutBottomControls().
            SizeChanged += (sender, e) => LayoutBottomControls();
            LayoutBottomControls();
        }

        // The dimensions of the form as laid out in the designer; the bottom-control
        // positions below are expressed as fractions of these.
        private const double kDesignWidth = 618.0;
        private const double kDesignHeight = 475.0;

        /// <summary>
        /// Re-positions the controls that hug the bottom of the splash (the SIL logo in the
        /// lower-right, and the version/feedback/copyright labels in the lower-left). We do
        /// this in code rather than relying on Bottom/Right anchoring because, on high-DPI
        /// systems—especially when the splash is created in one monitor's DPI context and then
        /// shown on a monitor with a different scale—WinForms mis-rescales bottom/right-anchored
        /// controls, stranding them up over the Bloom logo (BL-16452). The fractions below
        /// reproduce the original designer layout exactly when there is no DPI mismatch.
        /// </summary>
        private void LayoutBottomControls()
        {
            // Bottom-left text labels: left margin and vertical position kept proportional.
            PlaceTopLeftByFraction(_feedbackStatusLabel, 73, 318);
            PlaceTopLeftByFraction(_shortVersionLabel, 73, 342);
            PlaceTopLeftByFraction(_longVersionInfo, 73, 365);
            PlaceTopLeftByFraction(_copyrightlabel, 73, 388);

            // SIL logo, lower-right: its right edge sat at 544/618 across and its bottom at
            // 413/475 down in the design.
            var silLeft =
                (int)Math.Round(544.0 / kDesignWidth * ClientSize.Width) - pictureBox2.Width;
            var silTop =
                (int)Math.Round(413.0 / kDesignHeight * ClientSize.Height) - pictureBox2.Height;
            pictureBox2.Location = new System.Drawing.Point(silLeft, silTop);
        }

        /// <summary>
        /// Sets a control's top-left location to the given designer coordinates expressed as
        /// fractions of the design size, scaled to the current client size.
        /// </summary>
        private void PlaceTopLeftByFraction(Control control, int designX, int designY)
        {
            var x = (int)Math.Round(designX / kDesignWidth * ClientSize.Width);
            var y = (int)Math.Round(designY / kDesignHeight * ClientSize.Height);
            control.Location = new System.Drawing.Point(x, y);
        }

        private void _fadeOutTimer_Tick(object sender, EventArgs e)
        {
            if (Opacity <= 0)
            {
                //Close();
                //if were were showing a dialog (like to choose a new project), Close would close that dialog too!
                //I tried setting the splashform.owner to the dlg, but that wasn't allowed.
                Hide();
            }
            Opacity -= 0.20;
        }

        private void SplashScreen_Load(object sender, EventArgs e)
        {
            //try really hard to become top most. See http://stackoverflow.com/questions/5282588/how-can-i-bring-my-application-window-to-the-front
            TopMost = true;
            Focus();
            var channel = ApplicationUpdateSupport.ChannelName;
            _channelLabel.Visible = channel.ToLowerInvariant() != "release";
            _channelLabel.Text = channel; // No need to localize this: seen only by testers or special users (BL-4451)
            _copyrightlabel.Text = $"© 2011-{DateTime.Now.Year} SIL Global";
            BringToFront();
        }

        private void SplashScreen_Paint(object sender, PaintEventArgs e)
        {
            var borderWidth = 0;
            var color = Palette.BloomRed;
            ControlPaint.DrawBorder(
                e.Graphics,
                ClientRectangle,
                color,
                borderWidth,
                ButtonBorderStyle.Solid,
                color,
                borderWidth,
                ButtonBorderStyle.Solid,
                color,
                borderWidth,
                ButtonBorderStyle.Solid,
                color,
                borderWidth,
                ButtonBorderStyle.Solid
            );
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
            this.Icon = global::Bloom.Properties.Resources.BloomIcon;
        }

        private void label2_Click(object sender, EventArgs e) { }
    }
}
