namespace Bloom.Registration
{
	partial class RegistrationDialog
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RegistrationDialog));
			this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
			this.tableLayoutPanel4 = new System.Windows.Forms.TableLayoutPanel();
			this._howUsingLabel = new System.Windows.Forms.Label();
			this._howAreYouUsing = new System.Windows.Forms.TextBox();
			this.tableLayoutPanel3 = new System.Windows.Forms.TableLayoutPanel();
			this.label2 = new System.Windows.Forms.Label();
			this._emailLabel = new System.Windows.Forms.Label();
			this._email = new System.Windows.Forms.TextBox();
			this._organization = new System.Windows.Forms.TextBox();
			this._headingLabel = new System.Windows.Forms.Label();
			this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
			this.label6 = new System.Windows.Forms.Label();
			this.label5 = new System.Windows.Forms.Label();
			this._firstName = new System.Windows.Forms.TextBox();
			this._surname = new System.Windows.Forms.TextBox();
			this._additionalTextLabel = new SIL.Windows.Forms.Widgets.AutoHeightLabel();
			this._okButton = new System.Windows.Forms.Button();
			this.l10NSharpExtender1 = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._iAmStuckLabel = new System.Windows.Forms.LinkLabel();
			this._cancelButton = new System.Windows.Forms.Button();
			this._userIsStuckDetector = new System.Windows.Forms.Timer(this.components);
			this.tableLayoutPanel1.SuspendLayout();
			this.tableLayoutPanel4.SuspendLayout();
			this.tableLayoutPanel3.SuspendLayout();
			this.tableLayoutPanel2.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.l10NSharpExtender1)).BeginInit();
			this.SuspendLayout();
			// 
			// tableLayoutPanel1
			// 
			this.tableLayoutPanel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tableLayoutPanel1.ColumnCount = 1;
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tableLayoutPanel1.Controls.Add(this.tableLayoutPanel4, 0, 5);
			this.tableLayoutPanel1.Controls.Add(this.tableLayoutPanel3, 0, 4);
			this.tableLayoutPanel1.Controls.Add(this._headingLabel, 0, 0);
			this.tableLayoutPanel1.Controls.Add(this.tableLayoutPanel2, 0, 3);
			this.tableLayoutPanel1.Controls.Add(this._additionalTextLabel, 0, 1);
			this.tableLayoutPanel1.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.tableLayoutPanel1.Location = new System.Drawing.Point(22, 23);
			this.tableLayoutPanel1.Name = "tableLayoutPanel1";
			this.tableLayoutPanel1.RowCount = 6;
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.Size = new System.Drawing.Size(404, 333);
			this.tableLayoutPanel1.TabIndex = 1;
			// 
			// tableLayoutPanel4
			// 
			this.tableLayoutPanel4.ColumnCount = 1;
			this.tableLayoutPanel4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel4.Controls.Add(this._howUsingLabel, 0, 0);
			this.tableLayoutPanel4.Controls.Add(this._howAreYouUsing, 0, 1);
			this.tableLayoutPanel4.Location = new System.Drawing.Point(3, 201);
			this.tableLayoutPanel4.Name = "tableLayoutPanel4";
			this.tableLayoutPanel4.RowCount = 2;
			this.tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 50F));
			this.tableLayoutPanel4.Size = new System.Drawing.Size(401, 110);
			this.tableLayoutPanel4.TabIndex = 13;
			// 
			// _howUsingLabel
			// 
			this._howUsingLabel.AutoSize = true;
			this._howUsingLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._howUsingLabel, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._howUsingLabel, "Place a {0} where the name of the program goes.");
			this.l10NSharpExtender1.SetLocalizingId(this._howUsingLabel, "RegisterDialog.HowAreYouUsing");
			this._howUsingLabel.Location = new System.Drawing.Point(3, 20);
			this._howUsingLabel.Margin = new System.Windows.Forms.Padding(3, 20, 3, 0);
			this._howUsingLabel.Name = "_howUsingLabel";
			this._howUsingLabel.Size = new System.Drawing.Size(142, 17);
			this._howUsingLabel.TabIndex = 7;
			this._howUsingLabel.Text = "How are you using {0}?";
			// 
			// _howAreYouUsing
			// 
			this._howAreYouUsing.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._howAreYouUsing, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._howAreYouUsing, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._howAreYouUsing, L10NSharp.LocalizationPriority.NotLocalizable);
			this.l10NSharpExtender1.SetLocalizingId(this._howAreYouUsing, "RegistrationDialog._howAreYouUsing");
			this._howAreYouUsing.Location = new System.Drawing.Point(3, 40);
			this._howAreYouUsing.Multiline = true;
			this._howAreYouUsing.Name = "_howAreYouUsing";
			this._howAreYouUsing.Size = new System.Drawing.Size(395, 67);
			this._howAreYouUsing.TabIndex = 0;
			this._howAreYouUsing.TextChanged += new System.EventHandler(this.OnTextChanged);
			// 
			// tableLayoutPanel3
			// 
			this.tableLayoutPanel3.ColumnCount = 2;
			this.tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
			this.tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
			this.tableLayoutPanel3.Controls.Add(this.label2, 1, 0);
			this.tableLayoutPanel3.Controls.Add(this._emailLabel, 0, 0);
			this.tableLayoutPanel3.Controls.Add(this._email, 0, 1);
			this.tableLayoutPanel3.Controls.Add(this._organization, 1, 1);
			this.tableLayoutPanel3.Location = new System.Drawing.Point(3, 124);
			this.tableLayoutPanel3.Name = "tableLayoutPanel3";
			this.tableLayoutPanel3.RowCount = 2;
			this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel3.Size = new System.Drawing.Size(401, 71);
			this.tableLayoutPanel3.TabIndex = 12;
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this.label2, null);
			this.l10NSharpExtender1.SetLocalizationComment(this.label2, null);
			this.l10NSharpExtender1.SetLocalizingId(this.label2, "RegisterDialog.Organization");
			this.label2.Location = new System.Drawing.Point(203, 20);
			this.label2.Margin = new System.Windows.Forms.Padding(3, 20, 3, 0);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(83, 17);
			this.label2.TabIndex = 8;
			this.label2.Text = "Organization";
			// 
			// _emailLabel
			// 
			this._emailLabel.AutoSize = true;
			this._emailLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._emailLabel, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._emailLabel, null);
			this.l10NSharpExtender1.SetLocalizingId(this._emailLabel, "RegisterDialog.Email");
			this._emailLabel.Location = new System.Drawing.Point(3, 20);
			this._emailLabel.Margin = new System.Windows.Forms.Padding(3, 20, 3, 0);
			this._emailLabel.Name = "_emailLabel";
			this._emailLabel.Size = new System.Drawing.Size(91, 17);
			this._emailLabel.TabIndex = 7;
			this._emailLabel.Text = "Email Address";
			// 
			// _email
			// 
			this._email.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._email, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._email, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._email, L10NSharp.LocalizationPriority.NotLocalizable);
			this.l10NSharpExtender1.SetLocalizingId(this._email, "RegistrationDialog._email");
			this._email.Location = new System.Drawing.Point(3, 40);
			this._email.Name = "_email";
			this._email.Size = new System.Drawing.Size(177, 25);
			this._email.TabIndex = 0;
			// 
			// _organization
			// 
			this._organization.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._organization, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._organization, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._organization, L10NSharp.LocalizationPriority.NotLocalizable);
			this.l10NSharpExtender1.SetLocalizingId(this._organization, "RegistrationDialog._organization");
			this._organization.Location = new System.Drawing.Point(203, 40);
			this._organization.Name = "_organization";
			this._organization.Size = new System.Drawing.Size(181, 25);
			this._organization.TabIndex = 1;
			this._organization.TextChanged += new System.EventHandler(this.OnTextChanged);
			// 
			// _headingLabel
			// 
			this._headingLabel.AutoSize = true;
			this._headingLabel.Font = new System.Drawing.Font("Segoe UI", 12F);
			this.l10NSharpExtender1.SetLocalizableToolTip(this._headingLabel, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._headingLabel, "Place a {0} where the name of the program goes.");
			this.l10NSharpExtender1.SetLocalizingId(this._headingLabel, "RegisterDialog.Heading");
			this._headingLabel.Location = new System.Drawing.Point(3, 0);
			this._headingLabel.MaximumSize = new System.Drawing.Size(400, 0);
			this._headingLabel.Name = "_headingLabel";
			this._headingLabel.Size = new System.Drawing.Size(250, 21);
			this._headingLabel.TabIndex = 1;
			this._headingLabel.Text = "Please take a minute to register {0}";
			// 
			// tableLayoutPanel2
			// 
			this.tableLayoutPanel2.ColumnCount = 2;
			this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
			this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
			this.tableLayoutPanel2.Controls.Add(this.label6, 1, 0);
			this.tableLayoutPanel2.Controls.Add(this.label5, 0, 0);
			this.tableLayoutPanel2.Controls.Add(this._firstName, 0, 1);
			this.tableLayoutPanel2.Controls.Add(this._surname, 1, 1);
			this.tableLayoutPanel2.Location = new System.Drawing.Point(3, 47);
			this.tableLayoutPanel2.Name = "tableLayoutPanel2";
			this.tableLayoutPanel2.RowCount = 2;
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel2.Size = new System.Drawing.Size(401, 71);
			this.tableLayoutPanel2.TabIndex = 11;
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this.label6, null);
			this.l10NSharpExtender1.SetLocalizationComment(this.label6, null);
			this.l10NSharpExtender1.SetLocalizingId(this.label6, "RegisterDialog.Surname");
			this.label6.Location = new System.Drawing.Point(203, 20);
			this.label6.Margin = new System.Windows.Forms.Padding(3, 20, 3, 0);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(59, 17);
			this.label6.TabIndex = 8;
			this.label6.Text = "Surname";
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this.label5, null);
			this.l10NSharpExtender1.SetLocalizationComment(this.label5, null);
			this.l10NSharpExtender1.SetLocalizingId(this.label5, "RegisterDialog.FirstName");
			this.label5.Location = new System.Drawing.Point(3, 20);
			this.label5.Margin = new System.Windows.Forms.Padding(3, 20, 3, 0);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(71, 17);
			this.label5.TabIndex = 7;
			this.label5.Text = "First Name";
			// 
			// _firstName
			// 
			this._firstName.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._firstName, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._firstName, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._firstName, L10NSharp.LocalizationPriority.NotLocalizable);
			this.l10NSharpExtender1.SetLocalizingId(this._firstName, "RegistrationDialog._firstName");
			this._firstName.Location = new System.Drawing.Point(3, 40);
			this._firstName.Name = "_firstName";
			this._firstName.Size = new System.Drawing.Size(177, 25);
			this._firstName.TabIndex = 0;
			this._firstName.TextChanged += new System.EventHandler(this.OnTextChanged);
			// 
			// _surname
			// 
			this._surname.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._surname, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._surname, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._surname, L10NSharp.LocalizationPriority.NotLocalizable);
			this.l10NSharpExtender1.SetLocalizingId(this._surname, "RegistrationDialog._surname");
			this._surname.Location = new System.Drawing.Point(203, 40);
			this._surname.Name = "_surname";
			this._surname.Size = new System.Drawing.Size(181, 25);
			this._surname.TabIndex = 1;
			this._surname.TextChanged += new System.EventHandler(this.OnTextChanged);
			// 
			// _additionalTextLabel
			// 
			this._additionalTextLabel.AutoEllipsis = true;
			this._additionalTextLabel.Enabled = false;
			this._additionalTextLabel.ForeColor = System.Drawing.SystemColors.ControlText;
			this._additionalTextLabel.Image = null;
			this.l10NSharpExtender1.SetLocalizableToolTip(this._additionalTextLabel, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._additionalTextLabel, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._additionalTextLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this.l10NSharpExtender1.SetLocalizingId(this._additionalTextLabel, "RegistrationDialog._additionalTextLabel");
			this._additionalTextLabel.Location = new System.Drawing.Point(3, 31);
			this._additionalTextLabel.Margin = new System.Windows.Forms.Padding(3, 10, 3, 0);
			this._additionalTextLabel.Name = "_additionalTextLabel";
			this._additionalTextLabel.Size = new System.Drawing.Size(398, 13);
			this._additionalTextLabel.TabIndex = 14;
			this._additionalTextLabel.Text = "additionalText";
			// 
			// _okButton
			// 
			this._okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._okButton.Enabled = false;
			this._okButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._okButton, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._okButton, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._okButton, L10NSharp.LocalizationPriority.High);
			this.l10NSharpExtender1.SetLocalizingId(this._okButton, "RegisterDialog.RegisterButton");
			this._okButton.Location = new System.Drawing.Point(263, 362);
			this._okButton.Name = "_okButton";
			this._okButton.Size = new System.Drawing.Size(75, 27);
			this._okButton.TabIndex = 0;
			this._okButton.Text = "&Register";
			this._okButton.UseVisualStyleBackColor = true;
			this._okButton.Click += new System.EventHandler(this._okButton_Click);
			// 
			// l10NSharpExtender1
			// 
			this.l10NSharpExtender1.LocalizationManagerId = "Bloom";
			this.l10NSharpExtender1.PrefixForNewItems = null;
			// 
			// _iAmStuckLabel
			// 
			this._iAmStuckLabel.AutoSize = true;
			this.l10NSharpExtender1.SetLocalizableToolTip(this._iAmStuckLabel, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._iAmStuckLabel, null);
			this.l10NSharpExtender1.SetLocalizingId(this._iAmStuckLabel, "RegisterDialog.IAmStuckLabel");
			this._iAmStuckLabel.Location = new System.Drawing.Point(28, 370);
			this._iAmStuckLabel.Name = "_iAmStuckLabel";
			this._iAmStuckLabel.Size = new System.Drawing.Size(136, 13);
			this._iAmStuckLabel.TabIndex = 2;
			this._iAmStuckLabel.TabStop = true;
			this._iAmStuckLabel.Text = "I\'m stuck, I\'ll finish this later.";
			this._iAmStuckLabel.Visible = false;
			this._iAmStuckLabel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.OnIAmStuckLabel_LinkClicked);
			// 
			// _cancelButton
			// 
			this._cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._cancelButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.l10NSharpExtender1.SetLocalizableToolTip(this._cancelButton, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._cancelButton, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._cancelButton, L10NSharp.LocalizationPriority.High);
			this.l10NSharpExtender1.SetLocalizingId(this._cancelButton, "Common.CancelButton");
			this._cancelButton.Location = new System.Drawing.Point(348, 362);
			this._cancelButton.Name = "_cancelButton";
			this._cancelButton.Size = new System.Drawing.Size(75, 27);
			this._cancelButton.TabIndex = 3;
			this._cancelButton.Text = "&Cancel";
			this._cancelButton.UseVisualStyleBackColor = true;
			this._cancelButton.Click += new System.EventHandler(this._cancelButton_Click);
			// 
			// _userIsStuckDetector
			// 
			this._userIsStuckDetector.Tick += new System.EventHandler(this._userIsStuckDetector_Tick);
			// 
			// RegistrationDialog
			// 
			this.AcceptButton = this._okButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this._cancelButton;
			this.ClientSize = new System.Drawing.Size(447, 401);
			this.ControlBox = false;
			this.Controls.Add(this._cancelButton);
			this.Controls.Add(this._iAmStuckLabel);
			this.Controls.Add(this.tableLayoutPanel1);
			this.Controls.Add(this._okButton);
			this.Icon = global::Bloom.Properties.Resources.BloomIcon;
			this.l10NSharpExtender1.SetLocalizableToolTip(this, null);
			this.l10NSharpExtender1.SetLocalizationComment(this, "Place a {0} where the name of the program goes.");
			this.l10NSharpExtender1.SetLocalizingId(this, "RegisterDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "RegistrationDialog";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Register {0}";
			this.Load += new System.EventHandler(this.RegistrationDialog_Load);
			this.tableLayoutPanel1.ResumeLayout(false);
			this.tableLayoutPanel1.PerformLayout();
			this.tableLayoutPanel4.ResumeLayout(false);
			this.tableLayoutPanel4.PerformLayout();
			this.tableLayoutPanel3.ResumeLayout(false);
			this.tableLayoutPanel3.PerformLayout();
			this.tableLayoutPanel2.ResumeLayout(false);
			this.tableLayoutPanel2.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.l10NSharpExtender1)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
		private System.Windows.Forms.Label _headingLabel;
		private System.Windows.Forms.TextBox _firstName;
		private System.Windows.Forms.Button _okButton;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel4;
		private System.Windows.Forms.Label _howUsingLabel;
		private L10NSharp.UI.L10NSharpExtender l10NSharpExtender1;
		private System.Windows.Forms.TextBox _howAreYouUsing;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel3;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Label _emailLabel;
		private System.Windows.Forms.TextBox _email;
		private System.Windows.Forms.TextBox _organization;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.TextBox _surname;
		private System.Windows.Forms.LinkLabel _iAmStuckLabel;
		private System.Windows.Forms.Timer _userIsStuckDetector;
		private System.Windows.Forms.Button _cancelButton;
		private SIL.Windows.Forms.Widgets.AutoHeightLabel _additionalTextLabel;
	}
}
