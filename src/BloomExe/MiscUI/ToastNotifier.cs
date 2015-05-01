using System;
using System.Windows.Forms;

namespace Bloom.MiscUI
{
	/// <summary>
	/// Like a notification ballon, but more reliable "toast" because it slowly goes up, then down.
	/// Subscribe to the Click event to know if the user clicked on it.
	/// Note: currently clicking on the image or text invoke the click event, not just the action link.
	/// Note: don't use a lot of these. They hang around off-screen.  (See comment in GoDownTimerTick.)
	/// It's primarily intended for something like "updates are available" that happens
	/// once per run of the program.
	/// Usage:
	/// 		var notifier = new ToastNotifier();
	///			notifier.Image.Image = {some small Image, about 32x32};
	///			notifier.ToastClicked += (sender, args) => {do something};
	///			notifier.Show("You should know this", "Something you might do about it", {some # of seconds});
	///	You should NOT dispose of the notifier, since we haven't found a safe time to even close it.
	/// </summary>
	public partial class ToastNotifier : Form
	{
		private Timer _goUpTimer;
		private Timer _goDownTimer;
		private Timer _pauseTimer;
		private int startPosX;
		private int startPosY;
		private bool _stayUp;

		/// <summary>
		/// The user clicked on the toast popup
		/// </summary>
		public event EventHandler ToastClicked;



		/// <summary>
		/// constructor
		/// </summary>
		public ToastNotifier()
		{
			InitializeComponent();
			// We do NOT want our window to be the top most; that disables the effect of ShowWithoutActivation
			// and means that the toast steals focus from our main window. Once brought up in front of our
			// window (but not activated) it seems to stay there pretty nicely even while we interact with
			// the window. See BL-1126.
			//TopMost = true;
			// Pop doesn't need to be shown in task bar
			ShowInTaskbar = false;
			// Create and run timer for animation
			_goUpTimer = new Timer();
			_goUpTimer.Interval = 50;
			_goUpTimer.Tick += GoUpTimerTick;
			_goDownTimer = new Timer();
			_goDownTimer.Interval = 50;
			_goDownTimer.Tick += GoDownTimerTick;
			_pauseTimer = new Timer();
			_pauseTimer.Interval = 15000;
			_pauseTimer.Tick += PauseTimerTick;
		}

		/// <summary>
		/// Stops it stealing focus from the main window even though it will be in front of that window.
		/// </summary>
		protected override bool ShowWithoutActivation
		{
			get { return true; }
		}

		private void PauseTimerTick(object sender, EventArgs e)
		{
			_pauseTimer.Stop();
			_goDownTimer.Start();
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="e"></param>
		protected override void OnLoad(EventArgs e)
		{
			// Move window out of screen
			startPosX = Screen.PrimaryScreen.WorkingArea.Width - Width;
			startPosY = Screen.PrimaryScreen.WorkingArea.Height;
			SetDesktopLocation(startPosX, startPosY);
			base.OnLoad(e);
			// Begin animation
			_goUpTimer.Start();
		}

		void GoUpTimerTick(object sender, EventArgs e)
		{
			//Lift window by 5 pixels
			startPosY -= 5;
			//If window is fully visible stop the timer
			if (startPosY < Screen.PrimaryScreen.WorkingArea.Height - Height)
			{
				_goUpTimer.Stop();
				if(!_stayUp)
					_pauseTimer.Start();
			}
			else
				SetDesktopLocation(startPosX, startPosY);
		}

		private void GoDownTimerTick(object sender, EventArgs e)
		{
			//Lower window by 5 pixels
			startPosY += 5;
			//If window is fully visible stop the timer
			if (startPosY > Screen.PrimaryScreen.Bounds.Height)
			{
				_goDownTimer.Stop();
				//If the client app starts with a "show dialog" open (e.g., selecting a file to open), this Close() actually closes *that* dialog, which is, um, bad.
				//So I'm just going to not do the close, figuring that it only runs once per run of the application anyhow
				//Close();
			}
			else
				SetDesktopLocation(startPosX, startPosY);
		}

		private void ToastNotifier_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Yes;
			Close();
			EventHandler handler = ToastClicked;
			if (handler != null)
				handler(this, e);
		}

		/// <summary>
		/// Show the toast
		/// </summary>
		/// <param name="message"></param>
		/// <param name="callToAction">Text of the hyperlink </param>
		/// <param name="seconds">How long to show before it goes back down</param>
		public void Show(string message, string callToAction, int seconds)
		{
			_message.Text = message;
			_callToAction.Text = callToAction;
			if (seconds < 0)
			{
				_stayUp = true; //just leave it up permanently
			}
			else
			{
				_pauseTimer.Interval = 1000 * seconds;
			}
			Show();
		}

		private void callToAction_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			this.ToastNotifier_Click(null, null);
		}
	}
}
