using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;

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
		private int endPosY;	// minimum (highest on screen) desired value
		private bool _stayUp;
		private static string _currentMessage;

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

//		public Color BackgroundColor
//		{
//			set { this.color}
//		}
		/// <summary>
		/// Stops it stealing focus from the main window even though it will be in front of that window.
		/// </summary>
		protected override bool ShowWithoutActivation
		{
			get { return true; }
		}

		//storing this here so that we only make one and don't have to worry about disposing of it
		public static Image WarningBitmap = SystemIcons.Warning.ToBitmap();

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
			endPosY = startPosY - Height;
			SetDesktopLocation(startPosX, startPosY);
			//Debug.WriteLine(String.Format("DEBUG Toast.OnLoad(): after adjustments, Bounds = {0}", this.DesktopBounds));
			base.OnLoad(e);
			// Begin animation
			_goUpTimer.Start();
		}

		void GoUpTimerTick(object sender, EventArgs e)
		{
			//Debug.WriteLine(String.Format("DEBUG Begin Toast.GoUpTimerTick(): Bounds = {0}", this.DesktopBounds));
			//Lift window by 5 pixels
			startPosY -= 5;
			//If window is fully visible stop the timer
			if (startPosY < endPosY)
			{
				_goUpTimer.Stop();
				startPosY = endPosY;
				SetDesktopLocation(startPosX, startPosY);
				if(!_stayUp)
					_pauseTimer.Start();
			}
			else
			{
				SetDesktopLocation(startPosX, startPosY);
			}
			this.Refresh();
			//Debug.WriteLine(String.Format("DEBUG End Toast.GoUpTimerTick(): Bounds = {0}", this.DesktopBounds));
		}

		private void GoDownTimerTick(object sender, EventArgs e)
		{
			//Debug.WriteLine(String.Format("DEBUG Begin Toast.GoDownTimerTick(): Bounds = {0}", this.DesktopBounds));
			//Lower window by 5 pixels
			startPosY += 5;
			//If window is fully visible stop the timer
			if (startPosY > Screen.PrimaryScreen.Bounds.Height)
			{
#if __MonoCS__
				// The window doesn't move onto and off the screen on Wasta 14 Linux for some reason.  I suspect some sort of system setting,
				// not Mono, since it works fine on Trusty Linux and the Bounds values all look correct on Wasta 14.  So we'll just make this
				// form invisible at the right time.  This should not hurt anything on Trusty, but I'm marking it for Mono only just in case
				// setting Visible has the same wierd bug on Windows as calling Close().
				this.Visible = false;
#endif
				_goDownTimer.Stop();
				//If the client app starts with a "show dialog" open (e.g., selecting a file to open), this Close() actually closes *that* dialog, which is, um, bad.
				// But we've been closing in various other contexts using an idle event, and testing doesn't seem to show any problems with
				// that, and we're now making more and more use of the class, so leaving them to hang around is not good, either.
				// So far, haven't seen any problems with this approach.
				CloseThisLater();

				_currentMessage = null;

			}
			else
			{
				SetDesktopLocation(startPosX, startPosY);
				this.Refresh();
			}
			//Debug.WriteLine(String.Format("DEBUG End Toast.GoDownTimerTick(): Bounds = {0}", this.DesktopBounds));
		}

		private void ToastNotifier_Click(object sender, EventArgs e)
		{
			// Certain scenarios (like clicking to bring up a yellow screen) need these lines else the yellow screen
			// is closed by a close-this-dialog either already on the stack or about to be added by GoDownTimerTick.
			_goDownTimer.Tick -= GoDownTimerTick; //_goDownTimer.Stop() didn't work for some reason
			Application.Idle -= CloseThisCalledFromIdle;

			EventHandler handler = ToastClicked;
			ToastClicked = null;	// handle only one click message. (Linux posts two if link clicked, one for Toast window in general.)
			if (handler != null)
				handler(this, e);
			// We may still have a message pending for the sender even though it has already been disposed.
			// This would cause a System.ObjectDisposedException shortly if we call Close() directly from
			// here, so we add a method to close this ToastNotifier to the Application.Idle processor (which
			// won't fire until all of the current pending messages have been handled).  See BL-3285.
			CloseThisLater();
		}

		public void CloseSafely()
		{
			// It's only safe to close after we're sure the dialog will not get any more messages
			// (e.g., a tick after it's disposed)
			_goUpTimer.Tick -= GoUpTimerTick;
			_goDownTimer.Tick -= GoDownTimerTick; //_goDownTimer.Stop() didn't work for some reason
			Application.Idle -= CloseThisCalledFromIdle;
			CloseThisLater();
		}

		private void CloseThisLater()
		{
			Application.Idle += CloseThisCalledFromIdle;
		}

		private void CloseThisCalledFromIdle(object sender, EventArgs e)
		{
			Application.Idle -= CloseThisCalledFromIdle;
			DialogResult = DialogResult.Yes;
			this.Close();
		}

		/// <summary>
		/// Show the toast
		/// </summary>
		/// <param name="message"></param>
		/// <param name="callToAction">Text of the hyperlink </param>
		/// <param name="seconds">How long to show before it goes back down</param>
		public void Show(string message, string callToAction, int seconds)
		{
			//avoid showing a blizzard of the same message
			if(message == _currentMessage)
				return;
			_currentMessage = message;
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

		public void UpdateMessage(string newMessage)
		{
			_message.Text = newMessage;
		}

		private void callToAction_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			this.ToastNotifier_Click(null, null);
		}
	}
}
