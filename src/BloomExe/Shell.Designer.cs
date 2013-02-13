namespace Bloom
{
    partial class Shell
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Shell));
			this._contextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
			this._size800x600 = new System.Windows.Forms.ToolStripMenuItem();
			this._size1024x600 = new System.Windows.Forms.ToolStripMenuItem();
			this._size1024x768 = new System.Windows.Forms.ToolStripMenuItem();
			this._size1024x586 = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
			this._contextMenu.SuspendLayout();
			this.SuspendLayout();
			// 
			// _contextMenu
			// 
			this._contextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._size1024x586,
            this.toolStripMenuItem1,
            this._size800x600,
            this._size1024x600,
            this._size1024x768});
			this._contextMenu.Name = "_contextMenu";
			this._contextMenu.Size = new System.Drawing.Size(348, 120);
			// 
			// _size800x600
			// 
			this._size800x600.Name = "_size800x600";
			this._size800x600.Size = new System.Drawing.Size(347, 22);
			this._size800x600.Text = "800 x 600";
			this._size800x600.Click += new System.EventHandler(this.On800x600Click);
			// 
			// _size1024x600
			// 
			this._size1024x600.Name = "_size1024x600";
			this._size1024x600.Size = new System.Drawing.Size(347, 22);
			this._size1024x600.Text = "1024 x 600";
			this._size1024x600.Click += new System.EventHandler(this.On1024x600Click);
			// 
			// _size1024x768
			// 
			this._size1024x768.Name = "_size1024x768";
			this._size1024x768.Size = new System.Drawing.Size(347, 22);
			this._size1024x768.Text = "1024 x 768";
			this._size1024x768.Click += new System.EventHandler(this.On1024x768);
			// 
			// _size1024x586
			// 
			this._size1024x586.Name = "_size1024x586";
			this._size1024x586.Size = new System.Drawing.Size(347, 22);
			this._size1024x586.Text = "1024 x 586 Low-end netbook with windows Task bar";
			this._size1024x586.Click += new System.EventHandler(this.On1024x586);
			// 
			// toolStripMenuItem1
			// 
			this.toolStripMenuItem1.Name = "toolStripMenuItem1";
			this.toolStripMenuItem1.Size = new System.Drawing.Size(344, 6);
			// 
			// Shell
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.ClientSize = new System.Drawing.Size(1012, 548);
			this.ContextMenuStrip = this._contextMenu;
#if !__MonoCS__
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
#endif
			this.MinimumSize = new System.Drawing.Size(400, 300);
			this.Name = "Shell";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Bloom";
			this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
			this.Activated += new System.EventHandler(this.Shell_Activated);
			this.Deactivate += new System.EventHandler(this.Shell_Deactivate);
			this._contextMenu.ResumeLayout(false);
			this.ResumeLayout(false);

        }

        #endregion

		private System.Windows.Forms.ContextMenuStrip _contextMenu;
		private System.Windows.Forms.ToolStripMenuItem _size800x600;
		private System.Windows.Forms.ToolStripMenuItem _size1024x600;
		private System.Windows.Forms.ToolStripMenuItem _size1024x768;
		private System.Windows.Forms.ToolStripMenuItem _size1024x586;
		private System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;



	}
}