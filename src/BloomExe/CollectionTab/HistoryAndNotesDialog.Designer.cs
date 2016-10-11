#if CHORUS
namespace Bloom.CollectionTab
{
	partial class HistoryAndNotesDialog
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
			this.tabControl1 = new System.Windows.Forms.TabControl();
			this._historyPage = new System.Windows.Forms.TabPage();
			this._notesPage = new System.Windows.Forms.TabPage();
			this._closeButton = new System.Windows.Forms.Button();
			this.tabControl1.SuspendLayout();
			this.SuspendLayout();
			// 
			// tabControl1
			// 
			this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tabControl1.Controls.Add(this._historyPage);
			this.tabControl1.Controls.Add(this._notesPage);
			this.tabControl1.Location = new System.Drawing.Point(0, 0);
			this.tabControl1.Name = "tabControl1";
			this.tabControl1.SelectedIndex = 0;
			this.tabControl1.Size = new System.Drawing.Size(840, 455);
			this.tabControl1.TabIndex = 0;
			// 
			// _historyPage
			// 
			this._historyPage.Location = new System.Drawing.Point(4, 22);
			this._historyPage.Name = "_historyPage";
			this._historyPage.Padding = new System.Windows.Forms.Padding(3);
			this._historyPage.Size = new System.Drawing.Size(832, 429);
			this._historyPage.TabIndex = 0;
			this._historyPage.Text = "History";
			this._historyPage.UseVisualStyleBackColor = true;
			// 
			// _notesPage
			// 
			this._notesPage.Location = new System.Drawing.Point(4, 22);
			this._notesPage.Name = "_notesPage";
			this._notesPage.Padding = new System.Windows.Forms.Padding(3);
			this._notesPage.Size = new System.Drawing.Size(531, 451);
			this._notesPage.TabIndex = 1;
			this._notesPage.Text = "Notes";
			this._notesPage.UseVisualStyleBackColor = true;
			// 
			// _closeButton
			// 
			this._closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._closeButton.Location = new System.Drawing.Point(765, 473);
			this._closeButton.Name = "_closeButton";
			this._closeButton.Size = new System.Drawing.Size(75, 23);
			this._closeButton.TabIndex = 1;
			this._closeButton.Text = "&Close";
			this._closeButton.UseVisualStyleBackColor = true;
			// 
			// HistoryAndNotesDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this._closeButton;
			this.ClientSize = new System.Drawing.Size(852, 508);
			this.ControlBox = false;
			this.Controls.Add(this._closeButton);
			this.Controls.Add(this.tabControl1);
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "HistoryAndNotesDialog";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "History";
			this.Load += new System.EventHandler(this.HistoryAndNotesDialog_Load);
			this.tabControl1.ResumeLayout(false);
			this.ResumeLayout(false);

		}

#endregion

		private System.Windows.Forms.TabControl tabControl1;
		private System.Windows.Forms.TabPage _historyPage;
		private System.Windows.Forms.TabPage _notesPage;
		private System.Windows.Forms.Button _closeButton;
	}
}
#endif