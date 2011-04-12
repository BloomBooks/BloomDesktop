using Bloom.Edit;
using Bloom.Publish;

namespace Bloom.Project
{
    partial class ProjectView
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
            this.components = new System.ComponentModel.Container();
            this._tabControl = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this._infoTab = new System.Windows.Forms.TabPage();
            this._infoButton = new System.Windows.Forms.Button();
            this._openButton1 = new System.Windows.Forms.Button();
            this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this._tabControl.SuspendLayout();
            this.SuspendLayout();
            // 
            // _tabControl
            // 
            this._tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this._tabControl.Controls.Add(this.tabPage1);
            this._tabControl.Controls.Add(this.tabPage2);
            this._tabControl.Controls.Add(this.tabPage3);
            this._tabControl.Controls.Add(this._infoTab);
            this._tabControl.ItemSize = new System.Drawing.Size(43, 40);
            this._tabControl.Location = new System.Drawing.Point(0, 2);
            this._tabControl.Margin = new System.Windows.Forms.Padding(0);
            this._tabControl.Name = "_tabControl";
            this._tabControl.Padding = new System.Drawing.Point(0, 0);
            this._tabControl.SelectedIndex = 0;
            this._tabControl.Size = new System.Drawing.Size(885, 538);
            this._tabControl.TabIndex = 10;
            this._tabControl.SelectedIndexChanged += new System.EventHandler(this.tabControl1_SelectedIndexChanged);
            // 
            // tabPage1
            // 
            this.tabPage1.ImageIndex = 2;
            this.tabPage1.Location = new System.Drawing.Point(4, 44);
            this.tabPage1.Margin = new System.Windows.Forms.Padding(0);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Size = new System.Drawing.Size(877, 490);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.ToolTipText = "View Libaries";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // tabPage2
            // 
            this.tabPage2.ImageIndex = 1;
            this.tabPage2.Location = new System.Drawing.Point(4, 44);
            this.tabPage2.Margin = new System.Windows.Forms.Padding(0);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Size = new System.Drawing.Size(877, 490);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.ToolTipText = "Edit Book";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // tabPage3
            // 
            this.tabPage3.ImageIndex = 0;
            this.tabPage3.Location = new System.Drawing.Point(4, 44);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage3.Size = new System.Drawing.Size(877, 490);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.ToolTipText = "Publish Book";
            this.tabPage3.UseVisualStyleBackColor = true;
            // 
            // _infoTab
            // 
            this._infoTab.Location = new System.Drawing.Point(4, 44);
            this._infoTab.Name = "_infoTab";
            this._infoTab.Padding = new System.Windows.Forms.Padding(3);
            this._infoTab.Size = new System.Drawing.Size(877, 490);
            this._infoTab.TabIndex = 3;
            this._infoTab.UseVisualStyleBackColor = true;
            // 
            // _infoButton
            // 
            this._infoButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this._infoButton.BackColor = System.Drawing.Color.Transparent;
            this._infoButton.FlatAppearance.BorderSize = 0;
            this._infoButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._infoButton.Image = global::Bloom.Properties.Resources.info16x16;
            this._infoButton.Location = new System.Drawing.Point(815, 11);
            this._infoButton.Name = "_infoButton";
            this._infoButton.Size = new System.Drawing.Size(22, 23);
            this._infoButton.TabIndex = 12;
            this.toolTip1.SetToolTip(this._infoButton, "Get Information About Bloom");
            this._infoButton.UseVisualStyleBackColor = false;
            this._infoButton.Click += new System.EventHandler(this._infoButton_Click);
            // 
            // _openButton1
            // 
            this._openButton1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this._openButton1.BackColor = System.Drawing.Color.Transparent;
            this._openButton1.FlatAppearance.BorderSize = 0;
            this._openButton1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._openButton1.Image = global::Bloom.Properties.Resources.open;
            this._openButton1.Location = new System.Drawing.Point(850, 10);
            this._openButton1.Name = "_openButton1";
            this._openButton1.Size = new System.Drawing.Size(22, 23);
            this._openButton1.TabIndex = 13;
            this.toolTip1.SetToolTip(this._openButton1, "Open or Create Another Library");
            this._openButton1.UseVisualStyleBackColor = false;
            this._openButton1.Click += new System.EventHandler(this._openButton1_Click);
            // 
            // toolStripButton1
            // 
            this.toolStripButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton1.Image = global::Bloom.Properties.Resources.menuButton;
            this.toolStripButton1.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.toolStripButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton1.Name = "toolStripButton1";
            this.toolStripButton1.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton1.Text = "toolStripButton1";
            this.toolStripButton1.ToolTipText = "Open a library for a different language, or create a new library.";
            // 
            // ProjectView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._infoButton);
            this.Controls.Add(this._openButton1);
            this.Controls.Add(this._tabControl);
            this.Name = "ProjectView";
            this.Size = new System.Drawing.Size(885, 540);
            this._tabControl.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabControl _tabControl;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.TabPage tabPage3;
		private System.Windows.Forms.ToolStripButton toolStripButton1;
        private System.Windows.Forms.TabPage _infoTab;
        private System.Windows.Forms.Button _infoButton;
        private System.Windows.Forms.Button _openButton1;
        private System.Windows.Forms.ToolTip toolTip1;


    }
}