namespace Bloom.Edit
{
	partial class EditingView
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
			if (disposing)
			{
				if (_editButtonsUpdateTimer != null)
				{
					_editButtonsUpdateTimer.Stop();
					_editButtonsUpdateTimer.Dispose();
					_editButtonsUpdateTimer = null;
				}

				if (components != null)
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
            components = new System.ComponentModel.Container();
            _editButtonsUpdateTimer = new System.Windows.Forms.Timer(components);
            SuspendLayout();
            //
            // _editButtonsUpdateTimer
            //
            _editButtonsUpdateTimer.Tick += _editButtonsUpdateTimer_Tick;
            //
            // EditingView
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            Margin = new System.Windows.Forms.Padding(4);
            Name = "EditingView";
            Size = new System.Drawing.Size(1200, 561);
            ResumeLayout(false);

        }

        #endregion

		private System.Windows.Forms.Timer _editButtonsUpdateTimer;
	}
}
