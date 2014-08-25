namespace GeckofxHtmlToPdf
{
	partial class GeckofxHtmlToPdfComponent
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Component Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this._checkForPdfFinishedTimer = new System.Windows.Forms.Timer(this.components);
			this._checkForBrowserNavigatedTimer = new System.Windows.Forms.Timer(this.components);
			// 
			// _checkForPdfFinishedTimer
			// 
			this._checkForPdfFinishedTimer.Tick += new System.EventHandler(this.OnCheckForPdfFinishedTimer_Tick);
			// 
			// _checkForBrowserNavigatedTimer
			// 
			this._checkForBrowserNavigatedTimer.Interval = 50;
			this._checkForBrowserNavigatedTimer.Tick += new System.EventHandler(this.OnCheckForBrowserNavigatedTimerTick);

		}

		#endregion

		private System.Windows.Forms.Timer _checkForPdfFinishedTimer;
		private System.Windows.Forms.Timer _checkForBrowserNavigatedTimer;
	}
}
