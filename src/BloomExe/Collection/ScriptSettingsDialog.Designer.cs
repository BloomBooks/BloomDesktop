﻿namespace Bloom.Collection
{
	partial class ScriptSettingsDialog
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
			this._languageNameLabel = new System.Windows.Forms.Label();
			this._rtlLanguageCheckBox = new System.Windows.Forms.CheckBox();
			this._tallerLinesCheckBox = new System.Windows.Forms.CheckBox();
			this._lineSpacingCombo = new System.Windows.Forms.ComboBox();
			this._okButton = new System.Windows.Forms.Button();
			this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
			this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.flowLayoutPanel1.SuspendLayout();
			this.tableLayoutPanel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// _languageNameLabel
			// 
			this._languageNameLabel.AutoSize = true;
			this._languageNameLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
			this._L10NSharpExtender.SetLocalizableToolTip(this._languageNameLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._languageNameLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._languageNameLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._languageNameLabel, "ScriptSettingsDialog.languageNameLabel");
			this._languageNameLabel.Location = new System.Drawing.Point(3, 0);
			this._languageNameLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
			this._languageNameLabel.Name = "_languageNameLabel";
			this._languageNameLabel.Size = new System.Drawing.Size(118, 19);
			this._languageNameLabel.TabIndex = 1;
			this._languageNameLabel.Text = "Language Name";
			// 
			// _rtlLanguageCheckBox
			// 
			this._rtlLanguageCheckBox.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._rtlLanguageCheckBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._rtlLanguageCheckBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._rtlLanguageCheckBox, "ScriptSettingsDialog.RightToLeftScriptCheckBox");
			this._rtlLanguageCheckBox.Location = new System.Drawing.Point(3, 3);
			this._rtlLanguageCheckBox.Margin = new System.Windows.Forms.Padding(3, 3, 3, 6);
			this._rtlLanguageCheckBox.Name = "_rtlLanguageCheckBox";
			this._rtlLanguageCheckBox.Size = new System.Drawing.Size(175, 23);
			this._rtlLanguageCheckBox.TabIndex = 25;
			this._rtlLanguageCheckBox.Text = "This script is right to left";
			this._rtlLanguageCheckBox.UseVisualStyleBackColor = true;
			// 
			// _tallerLinesCheckBox
			// 
			this._tallerLinesCheckBox.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._tallerLinesCheckBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._tallerLinesCheckBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._tallerLinesCheckBox, "ScriptSettingsDialog.TallerLinesCheckBox");
			this._tallerLinesCheckBox.Location = new System.Drawing.Point(3, 35);
			this._tallerLinesCheckBox.Name = "_tallerLinesCheckBox";
			this._tallerLinesCheckBox.Size = new System.Drawing.Size(207, 23);
			this._tallerLinesCheckBox.TabIndex = 26;
			this._tallerLinesCheckBox.Text = "This script requires taller lines";
			this._tallerLinesCheckBox.UseVisualStyleBackColor = true;
			this._tallerLinesCheckBox.CheckedChanged += new System.EventHandler(this._tallerLinesCheckBox_CheckedChanged);
			// 
			// _lineSpacingCombo
			// 
			this._lineSpacingCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this._lineSpacingCombo.FormattingEnabled = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._lineSpacingCombo, null);
			this._L10NSharpExtender.SetLocalizationComment(this._lineSpacingCombo, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._lineSpacingCombo, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._lineSpacingCombo, "ScriptSettingsDialog.LineSpacingCombo");
			this._lineSpacingCombo.Location = new System.Drawing.Point(26, 64);
			this._lineSpacingCombo.Margin = new System.Windows.Forms.Padding(26, 3, 20, 20);
			this._lineSpacingCombo.Name = "_lineSpacingCombo";
			this._lineSpacingCombo.Size = new System.Drawing.Size(121, 25);
			this._lineSpacingCombo.TabIndex = 27;
			// 
			// _okButton
			// 
			this._okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._okButton.AutoSize = true;
			this._okButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
			this._L10NSharpExtender.SetLocalizableToolTip(this._okButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._okButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._okButton, "Common.OKButton");
			this._okButton.Location = new System.Drawing.Point(126, 147);
			this._okButton.MinimumSize = new System.Drawing.Size(90, 29);
			this._okButton.Name = "_okButton";
			this._okButton.Padding = new System.Windows.Forms.Padding(6, 0, 6, 0);
			this._okButton.Size = new System.Drawing.Size(90, 29);
			this._okButton.TabIndex = 3;
			this._okButton.Text = "&OK";
			this._okButton.UseVisualStyleBackColor = true;
			// 
			// flowLayoutPanel1
			// 
			this.flowLayoutPanel1.AutoSize = true;
			this.flowLayoutPanel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.flowLayoutPanel1.Controls.Add(this._languageNameLabel);
			this.flowLayoutPanel1.Controls.Add(this.tableLayoutPanel1);
			this.flowLayoutPanel1.Controls.Add(this._okButton);
			this.flowLayoutPanel1.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
			this.flowLayoutPanel1.Location = new System.Drawing.Point(30, 20);
			this.flowLayoutPanel1.Name = "flowLayoutPanel1";
			this.flowLayoutPanel1.Size = new System.Drawing.Size(219, 179);
			this.flowLayoutPanel1.TabIndex = 0;
			// 
			// tableLayoutPanel1
			// 
			this.tableLayoutPanel1.AutoSize = true;
			this.tableLayoutPanel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.tableLayoutPanel1.ColumnCount = 1;
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel1.Controls.Add(this._rtlLanguageCheckBox, 0, 0);
			this.tableLayoutPanel1.Controls.Add(this._tallerLinesCheckBox, 0, 1);
			this.tableLayoutPanel1.Controls.Add(this._lineSpacingCombo, 0, 2);
			this.tableLayoutPanel1.Location = new System.Drawing.Point(3, 32);
			this.tableLayoutPanel1.Name = "tableLayoutPanel1";
			this.tableLayoutPanel1.RowCount = 3;
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.Size = new System.Drawing.Size(213, 109);
			this.tableLayoutPanel1.TabIndex = 2;
			// 
			// ScriptSettingsDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.ClientSize = new System.Drawing.Size(345, 218);
			this.ControlBox = false;
			this.Controls.Add(this.flowLayoutPanel1);
			this.Font = new System.Drawing.Font("Segoe UI", 10F);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "ScriptSettingsDialog.ScriptSettingsWindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "ScriptSettingsDialog";
			this.Padding = new System.Windows.Forms.Padding(30, 20, 30, 26);
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Special Script Settings...";
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.flowLayoutPanel1.ResumeLayout(false);
			this.flowLayoutPanel1.PerformLayout();
			this.tableLayoutPanel1.ResumeLayout(false);
			this.tableLayoutPanel1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
		private System.Windows.Forms.Label _languageNameLabel;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
		private System.Windows.Forms.CheckBox _rtlLanguageCheckBox;
		private System.Windows.Forms.CheckBox _tallerLinesCheckBox;
		private System.Windows.Forms.ComboBox _lineSpacingCombo;
		private System.Windows.Forms.Button _okButton;
	}
}