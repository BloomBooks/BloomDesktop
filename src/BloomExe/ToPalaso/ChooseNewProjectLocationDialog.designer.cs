namespace Bloom.ToPalaso
{
	partial class ChooseNewProjectLocationDialog
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		protected readonly System.ComponentModel.IContainer components = null;

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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ChooseNewProjectLocationDialog));
			this.label1 = new System.Windows.Forms.Label();
			this.btnOK = new System.Windows.Forms.Button();
			this.btnCancel = new System.Windows.Forms.Button();
			this._textProjectName = new System.Windows.Forms.TextBox();
			this._pathLabel = new System.Windows.Forms.Label();
			this.SuspendLayout();
			//
			// label1
			//
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label1.Location = new System.Drawing.Point(16, 31);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(288, 16);
			this.label1.TabIndex = 1;
			this.label1.Text = "What would you like to call this project?";
			//
			// btnOK
			//
			this.btnOK.Location = new System.Drawing.Point(141, 127);
			this.btnOK.Name = "btnOK";
			this.btnOK.Size = new System.Drawing.Size(91, 29);
			this.btnOK.TabIndex = 2;
			this.btnOK.Text = "&OK";
			this.btnOK.UseVisualStyleBackColor = true;
			this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
			//
			// btnCancel
			//
			this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btnCancel.Location = new System.Drawing.Point(251, 127);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new System.Drawing.Size(88, 27);
			this.btnCancel.TabIndex = 3;
			this.btnCancel.Text = "&Cancel";
			this.btnCancel.UseVisualStyleBackColor = true;
			this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
			//
			// _textProjectName
			//
			this._textProjectName.Location = new System.Drawing.Point(19, 68);
			this._textProjectName.Name = "_textProjectName";
			this._textProjectName.Size = new System.Drawing.Size(320, 20);
			this._textProjectName.TabIndex = 0;
			this._textProjectName.TextChanged += new System.EventHandler(this._textProjectName_TextChanged);
			//
			// _pathLabel
			//
			this._pathLabel.AutoSize = true;
			this._pathLabel.ForeColor = System.Drawing.SystemColors.ControlDark;
			this._pathLabel.Location = new System.Drawing.Point(20, 94);
			this._pathLabel.Name = "_pathLabel";
			this._pathLabel.Size = new System.Drawing.Size(46, 13);
			this._pathLabel.TabIndex = 4;
			this._pathLabel.Text = "pathInfo";
			//
			// NewProject
			//
			this.AcceptButton = this.btnOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.AutoScroll = true;
			this.CancelButton = this.btnCancel;
			this.ClientSize = new System.Drawing.Size(359, 171);
			this.Controls.Add(this._pathLabel);
			this.Controls.Add(this._textProjectName);
			this.Controls.Add(this.btnCancel);
			this.Controls.Add(this.btnOK);
			this.Controls.Add(this.label1);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "ChooseNewProjectLocationDialog";
			this.Text = "New Project";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		protected System.Windows.Forms.Label label1;
		protected System.Windows.Forms.Button btnOK;
		protected System.Windows.Forms.Button btnCancel;
		protected System.Windows.Forms.TextBox _textProjectName;
		protected System.Windows.Forms.Label _pathLabel;
	}
}