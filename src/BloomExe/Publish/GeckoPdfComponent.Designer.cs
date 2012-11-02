namespace Bloom.Publish
{
    partial class GeckoPdfComponent
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
            this._checkForBrowserNavigatedTimer = new System.Windows.Forms.Timer(this.components);
            this._checkForPdfFinishedTimer = new System.Windows.Forms.Timer(this.components);
            // 
            // _checkForBrowserNavigatedTimer
            // 
            this._checkForBrowserNavigatedTimer.Interval = 3000;
            this._checkForBrowserNavigatedTimer.Tick += new System.EventHandler(this.OnCheckForBrowserNavigatedTimerTick);
            // 
            // _checkForPdfFinishedTimer
            // 
            this._checkForPdfFinishedTimer.Tick += new System.EventHandler(this._checkForPdfFinishedTimer_Tick);

        }

        #endregion

        private System.Windows.Forms.Timer _checkForBrowserNavigatedTimer;
        private System.Windows.Forms.Timer _checkForPdfFinishedTimer;
    }
}
