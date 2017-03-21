namespace Bloom.MiscUI
{
	partial class InstallationFailedDialog
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
			this._label = new System.Windows.Forms.Label();
			this._tableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
			this._linkLabel = new System.Windows.Forms.LinkLabel();
			this._closeButton = new System.Windows.Forms.Button();
			this._tableLayoutPanel.SuspendLayout();
			this.SuspendLayout();
			// 
			// _label
			// 
			this._label.AutoSize = true;
			this._label.Location = new System.Drawing.Point(3, 0);
			this._label.Name = "_label";
			this._label.Size = new System.Drawing.Size(448, 13);
			this._label.TabIndex = 0;
			this._label.Text = "Bloom failed during installation. You can use our installation guide to troublesh" +
    "oot this problem.";
			// 
			// _tableLayoutPanel
			// 
			this._tableLayoutPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._tableLayoutPanel.ColumnCount = 1;
			this._tableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this._tableLayoutPanel.Controls.Add(this._label, 0, 0);
			this._tableLayoutPanel.Controls.Add(this._linkLabel, 0, 2);
			this._tableLayoutPanel.Location = new System.Drawing.Point(12, 12);
			this._tableLayoutPanel.Name = "_tableLayoutPanel";
			this._tableLayoutPanel.RowCount = 3;
			this._tableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this._tableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 10F));
			this._tableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this._tableLayoutPanel.Size = new System.Drawing.Size(456, 78);
			this._tableLayoutPanel.TabIndex = 1;
			// 
			// _linkLabel
			// 
			this._linkLabel.AutoSize = true;
			this._linkLabel.Location = new System.Drawing.Point(3, 23);
			this._linkLabel.Name = "_linkLabel";
			this._linkLabel.Size = new System.Drawing.Size(321, 13);
			this._linkLabel.TabIndex = 1;
			this._linkLabel.TabStop = true;
			this._linkLabel.Text = "https://community.software.sil.org/t/how-to-fix-installation-problems";
			this._linkLabel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._linkLabel_LinkClicked);
			// 
			// _closeButton
			// 
			this._closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._closeButton.Location = new System.Drawing.Point(393, 98);
			this._closeButton.Name = "_closeButton";
			this._closeButton.Size = new System.Drawing.Size(75, 23);
			this._closeButton.TabIndex = 2;
			this._closeButton.Text = "Close";
			this._closeButton.UseVisualStyleBackColor = true;
			// 
			// InstallationFailedDialog
			// 
			this.AcceptButton = this._closeButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this._closeButton;
			this.ClientSize = new System.Drawing.Size(480, 133);
			this.Controls.Add(this._closeButton);
			this.Controls.Add(this._tableLayoutPanel);
			this.MaximizeBox = false;
			this.MaximumSize = new System.Drawing.Size(496, 172);
			this.MinimizeBox = false;
			this.MinimumSize = new System.Drawing.Size(496, 172);
			this.Name = "InstallationFailedDialog";
			this.Text = "Bloom Installation Failed";
			this._tableLayoutPanel.ResumeLayout(false);
			this._tableLayoutPanel.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Label _label;
		private System.Windows.Forms.TableLayoutPanel _tableLayoutPanel;
		private System.Windows.Forms.LinkLabel _linkLabel;
		private System.Windows.Forms.Button _closeButton;
	}
}