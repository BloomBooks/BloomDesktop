namespace Bloom.CollectionCreating
{
	partial class LanguageLocationControl
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
			this._districtText = new System.Windows.Forms.TextBox();
			this._provinceText = new System.Windows.Forms.TextBox();
			this._countryText = new System.Windows.Forms.TextBox();
			this._countryLabel = new System.Windows.Forms.Label();
			this._districtLabel = new System.Windows.Forms.Label();
			this._provinceLabel = new System.Windows.Forms.Label();
			this.betterLabel1 = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this.SuspendLayout();
			// 
			// _districtText
			// 
			this._districtText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._districtText.Location = new System.Drawing.Point(7, 154);
			this._districtText.Name = "_districtText";
			this._districtText.Size = new System.Drawing.Size(214, 25);
			this._districtText.TabIndex = 2;
			// 
			// _provinceText
			// 
			this._provinceText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._provinceText.Location = new System.Drawing.Point(7, 89);
			this._provinceText.Name = "_provinceText";
			this._provinceText.Size = new System.Drawing.Size(214, 25);
			this._provinceText.TabIndex = 1;
			// 
			// _countryText
			// 
			this._countryText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._countryText.Location = new System.Drawing.Point(7, 22);
			this._countryText.Name = "_countryText";
			this._countryText.Size = new System.Drawing.Size(214, 25);
			this._countryText.TabIndex = 0;
			// 
			// _countryLabel
			// 
			this._countryLabel.AutoSize = true;
			this._countryLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._countryLabel.Location = new System.Drawing.Point(3, 0);
			this._countryLabel.Name = "_countryLabel";
			this._countryLabel.Size = new System.Drawing.Size(59, 19);
			this._countryLabel.TabIndex = 8;
			this._countryLabel.Text = "Country";
			// 
			// _districtLabel
			// 
			this._districtLabel.AutoSize = true;
			this._districtLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._districtLabel.Location = new System.Drawing.Point(3, 132);
			this._districtLabel.Name = "_districtLabel";
			this._districtLabel.Size = new System.Drawing.Size(55, 19);
			this._districtLabel.TabIndex = 7;
			this._districtLabel.Text = "District";
			// 
			// _provinceLabel
			// 
			this._provinceLabel.AutoSize = true;
			this._provinceLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._provinceLabel.Location = new System.Drawing.Point(3, 67);
			this._provinceLabel.Name = "_provinceLabel";
			this._provinceLabel.Size = new System.Drawing.Size(63, 19);
			this._provinceLabel.TabIndex = 6;
			this._provinceLabel.Text = "Province";
			// 
			// betterLabel1
			// 
			this.betterLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.betterLabel1.BackColor = System.Drawing.SystemColors.Control;
			this.betterLabel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.betterLabel1.Font = new System.Drawing.Font("Segoe UI", 9F);
			this.betterLabel1.ForeColor = System.Drawing.Color.DimGray;
			this.betterLabel1.Location = new System.Drawing.Point(242, 22);
			this.betterLabel1.Multiline = true;
			this.betterLabel1.Name = "betterLabel1";
			this.betterLabel1.ReadOnly = true;
			this.betterLabel1.Size = new System.Drawing.Size(158, 177);
			this.betterLabel1.TabIndex = 9;
			this.betterLabel1.TabStop = false;
			this.betterLabel1.Text = "These are optional. Bloom will place them in the right places on title page of bo" +
    "oks you create.";
			// 
			// LanguageLocationControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.White;
			this.Controls.Add(this.betterLabel1);
			this.Controls.Add(this._districtText);
			this.Controls.Add(this._provinceText);
			this.Controls.Add(this._countryText);
			this.Controls.Add(this._countryLabel);
			this.Controls.Add(this._districtLabel);
			this.Controls.Add(this._provinceLabel);
			this.Name = "LanguageLocationControl";
			this.Size = new System.Drawing.Size(414, 343);
			this.Leave += new System.EventHandler(this.LanguageLocationControl_Leave);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox _districtText;
		private System.Windows.Forms.TextBox _provinceText;
		private System.Windows.Forms.TextBox _countryText;
		private System.Windows.Forms.Label _countryLabel;
		private System.Windows.Forms.Label _districtLabel;
		private System.Windows.Forms.Label _provinceLabel;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel betterLabel1;
	}
}
