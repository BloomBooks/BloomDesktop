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
