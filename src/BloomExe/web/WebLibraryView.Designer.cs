namespace Bloom.Library
{
	partial class WebLibraryView
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
			this.SuspendLayout();
			// 
			// WebLibraryView
			// 
			this.BackColor = System.Drawing.SystemColors.Control;
			this.Name = "WebLibraryView";
			this.Size = new System.Drawing.Size(773, 518);
			this.Load += new System.EventHandler(this.WebLibraryView_Load);
			this.VisibleChanged += new System.EventHandler(this.LibraryView_VisibleChanged);
			this.ResumeLayout(false);

        }

        #endregion





	}
}
