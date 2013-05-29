namespace Bloom.MiscUI
{
	partial class TipDialog
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
			this._acceptButton = new System.Windows.Forms.Button();
			this.tableLayout = new System.Windows.Forms.TableLayoutPanel();
			this._icon = new System.Windows.Forms.PictureBox();
			this._message = new System.Windows.Forms.TextBox();
			this.dontShowThisAgainButton1 = new DontShowThisAgainButton();
			this.tableLayout.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._icon)).BeginInit();
			this.SuspendLayout();
			//
			// _acceptButton
			//
			this._acceptButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._acceptButton.AutoSize = true;
			this._acceptButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._acceptButton.Location = new System.Drawing.Point(328, 59);
			this._acceptButton.Margin = new System.Windows.Forms.Padding(4, 0, 0, 15);
			this._acceptButton.Name = "_acceptButton";
			this._acceptButton.Size = new System.Drawing.Size(75, 26);
			this._acceptButton.TabIndex = 0;
			this._acceptButton.Text = "&OK";
			this._acceptButton.UseVisualStyleBackColor = true;
			this._acceptButton.Click += new System.EventHandler(this._acceptButton_Click);
			//
			// tableLayout
			//
			this.tableLayout.AutoSize = true;
			this.tableLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.tableLayout.BackColor = System.Drawing.Color.Transparent;
			this.tableLayout.ColumnCount = 4;
			this.tableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tableLayout.Controls.Add(this._icon, 0, 0);
			this.tableLayout.Controls.Add(this._acceptButton, 3, 1);
			this.tableLayout.Controls.Add(this._message, 1, 0);
			this.tableLayout.Controls.Add(this.dontShowThisAgainButton1, 0, 1);
			this.tableLayout.Location = new System.Drawing.Point(20, 22);
			this.tableLayout.Name = "tableLayout";
			this.tableLayout.RowCount = 2;
			this.tableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayout.Size = new System.Drawing.Size(403, 100);
			this.tableLayout.TabIndex = 5;
			//
			// _icon
			//
			this._icon.Location = new System.Drawing.Point(0, 0);
			this._icon.Margin = new System.Windows.Forms.Padding(0, 0, 15, 23);
			this._icon.Name = "_icon";
			this._icon.Size = new System.Drawing.Size(45, 36);
			this._icon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
			this._icon.TabIndex = 1;
			this._icon.TabStop = false;
			//
			// _message
			//
			this._message.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
			| System.Windows.Forms.AnchorStyles.Right)));
			this._message.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.tableLayout.SetColumnSpan(this._message, 3);
			this._message.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._message.Location = new System.Drawing.Point(60, 0);
			this._message.Margin = new System.Windows.Forms.Padding(0, 0, 0, 23);
			this._message.Multiline = true;
			this._message.Name = "_message";
			this._message.ReadOnly = true;
			this._message.Size = new System.Drawing.Size(343, 36);
			this._message.TabIndex = 0;
			this._message.Text = "Blah blah";
			this._message.TextChanged += new System.EventHandler(this.HandleMessageTextChanged);
			//
			// dontShowThisAgainButton1
			//
			this.dontShowThisAgainButton1.AutoSize = true;
			this.tableLayout.SetColumnSpan(this.dontShowThisAgainButton1, 2);
			this.dontShowThisAgainButton1.Location = new System.Drawing.Point(3, 62);
			this.dontShowThisAgainButton1.Name = "dontShowThisAgainButton1";
			this.dontShowThisAgainButton1.Size = new System.Drawing.Size(134, 17);
			this.dontShowThisAgainButton1.TabIndex = 2;
			this.dontShowThisAgainButton1.Text = "Don\'t Show This Again";
			this.dontShowThisAgainButton1.UseVisualStyleBackColor = true;
			//
			// TipDialog
			//
			this.AcceptButton = this._acceptButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.SystemColors.Control;
			this.CancelButton = this._acceptButton;
			this.ClientSize = new System.Drawing.Size(444, 140);
			this.ControlBox = false;
			this.Controls.Add(this.tableLayout);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.MinimumSize = new System.Drawing.Size(450, 38);
			this.Name = "TipDialog";
			this.Padding = new System.Windows.Forms.Padding(20, 22, 15, 0);
			this.ShowIcon = false;
			this.Text = "Tip";
			this.TopMost = true;
			this.Load += new System.EventHandler(this.TipDialog_Load);
			this.tableLayout.ResumeLayout(false);
			this.tableLayout.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._icon)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.PictureBox _icon;
		internal System.Windows.Forms.Button _acceptButton;
		private System.Windows.Forms.TableLayoutPanel tableLayout;
		private System.Windows.Forms.TextBox _message;
		private DontShowThisAgainButton dontShowThisAgainButton1;
	}
}