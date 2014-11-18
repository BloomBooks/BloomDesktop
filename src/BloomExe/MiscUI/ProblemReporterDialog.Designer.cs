namespace Bloom.MiscUI
{
	partial class ProblemReporterDialog
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
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this.label1 = new System.Windows.Forms.Label();
			this._description = new System.Windows.Forms.TextBox();
			this._name = new System.Windows.Forms.TextBox();
			this.label2 = new System.Windows.Forms.Label();
			this._email = new System.Windows.Forms.TextBox();
			this.label3 = new System.Windows.Forms.Label();
			this._includeScreenshot = new System.Windows.Forms.CheckBox();
			this._screenshotHolder = new System.Windows.Forms.PictureBox();
			this._submitButton = new System.Windows.Forms.Button();
			this._includeBook = new System.Windows.Forms.CheckBox();
			this._seeDetails = new System.Windows.Forms.LinkLabel();
			this._cancelButton = new System.Windows.Forms.Button();
			this._status = new Bloom.HtmlLabel();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._screenshotHolder)).BeginInit();
			this.SuspendLayout();
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "ReportProblemDialog";
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "ReportProblemDialog.WhatsTheProblem");
			this.label1.Location = new System.Drawing.Point(19, 64);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(184, 15);
			this.label1.TabIndex = 0;
			this.label1.Text = "What seems to be the problem?";
			// 
			// _description
			// 
			this._description.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._description, null);
			this._L10NSharpExtender.SetLocalizationComment(this._description, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._description, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._description, "ReportProblemDialog.ProblemReporterDialog._description");
			this._description.Location = new System.Drawing.Point(22, 84);
			this._description.Multiline = true;
			this._description.Name = "_description";
			this._description.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this._description.Size = new System.Drawing.Size(436, 105);
			this._description.TabIndex = 2;
			this._description.TextChanged += new System.EventHandler(this.UpdateDisplay);
			// 
			// _name
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._name, null);
			this._L10NSharpExtender.SetLocalizationComment(this._name, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._name, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._name, "ReportProblemDialog.ProblemReporterDialog._name");
			this._name.Location = new System.Drawing.Point(21, 32);
			this._name.Name = "_name";
			this._name.Size = new System.Drawing.Size(215, 20);
			this._name.TabIndex = 0;
			this._name.TextChanged += new System.EventHandler(this.UpdateDisplay);
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.label2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label2, null);
			this._L10NSharpExtender.SetLocalizingId(this.label2, "ReportProblemDialog.Name");
			this.label2.Location = new System.Drawing.Point(18, 12);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(40, 15);
			this.label2.TabIndex = 2;
			this.label2.Text = "Name";
			// 
			// _email
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._email, null);
			this._L10NSharpExtender.SetLocalizationComment(this._email, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._email, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._email, "ReportProblemDialog.ProblemReporterDialog._email");
			this._email.Location = new System.Drawing.Point(242, 32);
			this._email.Name = "_email";
			this._email.Size = new System.Drawing.Size(218, 20);
			this._email.TabIndex = 1;
			this._email.TextChanged += new System.EventHandler(this.UpdateDisplay);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.label3, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label3, null);
			this._L10NSharpExtender.SetLocalizingId(this.label3, "ReportProblemDialog.Email");
			this.label3.Location = new System.Drawing.Point(239, 12);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(36, 15);
			this.label3.TabIndex = 4;
			this.label3.Text = "Email";
			// 
			// _includeScreenshot
			// 
			this._includeScreenshot.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._includeScreenshot.AutoSize = true;
			this._includeScreenshot.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._L10NSharpExtender.SetLocalizableToolTip(this._includeScreenshot, null);
			this._L10NSharpExtender.SetLocalizationComment(this._includeScreenshot, null);
			this._L10NSharpExtender.SetLocalizingId(this._includeScreenshot, "ReportProblemDialog.IncludeScreenshotButton");
			this._includeScreenshot.Location = new System.Drawing.Point(21, 217);
			this._includeScreenshot.Name = "_includeScreenshot";
			this._includeScreenshot.Size = new System.Drawing.Size(154, 19);
			this._includeScreenshot.TabIndex = 4;
			this._includeScreenshot.Text = "Include this screenshot";
			this._includeScreenshot.UseVisualStyleBackColor = true;
			this._includeScreenshot.CheckedChanged += new System.EventHandler(this.UpdateDisplay);
			// 
			// _screenshotHolder
			// 
			this._screenshotHolder.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._screenshotHolder, null);
			this._L10NSharpExtender.SetLocalizationComment(this._screenshotHolder, null);
			this._L10NSharpExtender.SetLocalizingId(this._screenshotHolder, "pictureBox1");
			this._screenshotHolder.Location = new System.Drawing.Point(21, 247);
			this._screenshotHolder.Name = "_screenshotHolder";
			this._screenshotHolder.Size = new System.Drawing.Size(437, 156);
			this._screenshotHolder.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this._screenshotHolder.TabIndex = 8;
			this._screenshotHolder.TabStop = false;
			// 
			// _submitButton
			// 
			this._submitButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._submitButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._submitButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._submitButton, "ReportProblemDialog.SubmitButton");
			this._submitButton.Location = new System.Drawing.Point(361, 467);
			this._submitButton.Name = "_submitButton";
			this._submitButton.Size = new System.Drawing.Size(97, 23);
			this._submitButton.TabIndex = 5;
			this._submitButton.Text = "&Submit";
			this._submitButton.UseVisualStyleBackColor = true;
			this._submitButton.Click += new System.EventHandler(this._okButton_Click);
			// 
			// _includeBook
			// 
			this._includeBook.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._includeBook.AutoSize = true;
			this._includeBook.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._L10NSharpExtender.SetLocalizableToolTip(this._includeBook, null);
			this._L10NSharpExtender.SetLocalizationComment(this._includeBook, null);
			this._L10NSharpExtender.SetLocalizingId(this._includeBook, "ReportProblemDialog.IncludeBookButton");
			this._includeBook.Location = new System.Drawing.Point(21, 195);
			this._includeBook.MinimumSize = new System.Drawing.Size(440, 20);
			this._includeBook.Name = "_includeBook";
			this._includeBook.Size = new System.Drawing.Size(440, 20);
			this._includeBook.TabIndex = 3;
			this._includeBook.Text = "Include Book \'{0}\'";
			this._includeBook.UseVisualStyleBackColor = true;
			// 
			// _seeDetails
			// 
			this._seeDetails.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._seeDetails.AutoSize = true;
			this._seeDetails.LinkColor = System.Drawing.Color.Black;
			this._L10NSharpExtender.SetLocalizableToolTip(this._seeDetails, null);
			this._L10NSharpExtender.SetLocalizationComment(this._seeDetails, null);
			this._L10NSharpExtender.SetLocalizingId(this._seeDetails, "ReportProblemDialog.SeeDetails");
			this._seeDetails.Location = new System.Drawing.Point(19, 472);
			this._seeDetails.Name = "_seeDetails";
			this._seeDetails.Size = new System.Drawing.Size(154, 13);
			this._seeDetails.TabIndex = 5;
			this._seeDetails.TabStop = true;
			this._seeDetails.Text = "See what else will be submitted";
			this._seeDetails.Visible = false;
			this._seeDetails.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._seeDetails_LinkClicked);
			// 
			// _cancelButton
			// 
			this._cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._L10NSharpExtender.SetLocalizableToolTip(this._cancelButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._cancelButton, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._cancelButton, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._cancelButton, "ReportProblemDialog.ProblemReporterDialog._cancelButton");
			this._cancelButton.Location = new System.Drawing.Point(274, 127);
			this._cancelButton.Name = "_cancelButton";
			this._cancelButton.Size = new System.Drawing.Size(172, 23);
			this._cancelButton.TabIndex = 29;
			this._cancelButton.Text = "Intentionally invisible Cancel for Escape";
			this._cancelButton.UseVisualStyleBackColor = true;
			this._cancelButton.Click += new System.EventHandler(this._cancelButton_Click);
			// 
			// _status
			// 
			this._status.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._status.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._status.BackColor = System.Drawing.SystemColors.Control;
			this._status.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._status.HTML = null;
			this._L10NSharpExtender.SetLocalizableToolTip(this._status, null);
			this._L10NSharpExtender.SetLocalizationComment(this._status, null);
			this._L10NSharpExtender.SetLocalizingId(this._status, "ReportProblemDialog.HtmlLabel");
			this._status.Location = new System.Drawing.Point(21, 406);
			this._status.Margin = new System.Windows.Forms.Padding(0);
			this._status.Name = "_status";
			this._status.Size = new System.Drawing.Size(337, 84);
			this._status.TabIndex = 28;
			// 
			// ProblemReporterDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this._cancelButton;
			this.ClientSize = new System.Drawing.Size(487, 512);
			this.Controls.Add(this._seeDetails);
			this.Controls.Add(this._includeBook);
			this.Controls.Add(this._submitButton);
			this.Controls.Add(this._screenshotHolder);
			this.Controls.Add(this._includeScreenshot);
			this.Controls.Add(this._email);
			this.Controls.Add(this.label3);
			this.Controls.Add(this._name);
			this.Controls.Add(this.label2);
			this.Controls.Add(this._description);
			this.Controls.Add(this.label1);
			this.Controls.Add(this._cancelButton);
			this.Controls.Add(this._status);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "ReportProblemDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.MinimumSize = new System.Drawing.Size(503, 525);
			this.Name = "ProblemReporterDialog";
			this.ShowIcon = false;
			this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
			this.Text = "Report A Problem";
			this.Load += new System.EventHandler(this.ProblemReporterDialog_Load);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this._screenshotHolder)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox _description;
		private System.Windows.Forms.TextBox _name;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox _email;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.CheckBox _includeScreenshot;
		private System.Windows.Forms.PictureBox _screenshotHolder;
		private System.Windows.Forms.Button _submitButton;
		private System.Windows.Forms.CheckBox _includeBook;
		private System.Windows.Forms.LinkLabel _seeDetails;
		private HtmlLabel _status;
		private System.Windows.Forms.Button _cancelButton;
	}
}