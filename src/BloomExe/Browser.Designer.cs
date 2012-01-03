namespace Bloom
{
    partial class Browser
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;



        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
			this.components = new System.ComponentModel.Container();
			this._updateCommandsTimer = new System.Windows.Forms.Timer(this.components);
			this._setInitialFocusTimer = new System.Windows.Forms.Timer(this.components);
			this._afterValidatingTimer = new System.Windows.Forms.Timer(this.components);
			this.SuspendLayout();
			// 
			// _updateCommandsTimer
			// 
			this._updateCommandsTimer.Enabled = true;
			this._updateCommandsTimer.Tick += new System.EventHandler(this.OnUpdateDisplayTick);
			// 
			// _setInitialFocusTimer
			// 
			this._setInitialFocusTimer.Interval = 500;
			// 
			// _afterValidatingTimer
			// 
			this._afterValidatingTimer.Tick += new System.EventHandler(this._afterValidatingTimer_Tick);
			// 
			// Browser
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Name = "Browser";
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Timer _updateCommandsTimer;
        private System.Windows.Forms.Timer _setInitialFocusTimer;
		private System.Windows.Forms.Timer _afterValidatingTimer;
    }
}
