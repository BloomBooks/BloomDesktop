namespace Bloom.Workspace
{
	partial class ZoomControl
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
			this._minusButton = new System.Windows.Forms.Button();
			this._plusButton = new System.Windows.Forms.Button();
			this._percentLabel = new System.Windows.Forms.Label();
			this.SuspendLayout();
			// 
			// _minusButton
			// 
			this._minusButton.BackColor = System.Drawing.Color.Transparent;
			this._minusButton.FlatAppearance.BorderSize = 0;
			this._minusButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._minusButton.Location = new System.Drawing.Point(0, -2);
			this._minusButton.Margin = new System.Windows.Forms.Padding(0);
			this._minusButton.Name = "_minusButton";
			this._minusButton.Size = new System.Drawing.Size(14, 19);
			this._minusButton.TabIndex = 0;
			this._minusButton.Text = "-";
			this._minusButton.TextAlign = System.Drawing.ContentAlignment.TopCenter;
			this._minusButton.UseVisualStyleBackColor = false;
			this._minusButton.Click += new System.EventHandler(this._minusButton_Click);
			// 
			// _plusButton
			// 
			this._plusButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._plusButton.BackColor = System.Drawing.Color.Transparent;
			this._plusButton.FlatAppearance.BorderSize = 0;
			this._plusButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._plusButton.Location = new System.Drawing.Point(57, -2);
			this._plusButton.Name = "_plusButton";
			this._plusButton.Size = new System.Drawing.Size(17, 19);
			this._plusButton.TabIndex = 1;
			this._plusButton.Text = "+";
			this._plusButton.UseVisualStyleBackColor = false;
			this._plusButton.Click += new System.EventHandler(this._plusButton_Click);
			// 
			// _percentLabel
			// 
			this._percentLabel.Location = new System.Drawing.Point(17, 3);
			this._percentLabel.Name = "_percentLabel";
			this._percentLabel.Size = new System.Drawing.Size(35, 13);
			this._percentLabel.TabIndex = 2;
			this._percentLabel.Text = "100%";
			// 
			// ZoomControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._percentLabel);
			this.Controls.Add(this._plusButton);
			this.Controls.Add(this._minusButton);
			this.MinimumSize = new System.Drawing.Size(74, 17);
			this.Name = "ZoomControl";
			this.Size = new System.Drawing.Size(74, 17);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Button _minusButton;
		private System.Windows.Forms.Button _plusButton;
		private System.Windows.Forms.Label _percentLabel;
	}
}
