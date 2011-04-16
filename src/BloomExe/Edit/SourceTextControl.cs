using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.Edit
{
	public partial class SourceTextControl : UserControl
	{
		public SourceTextControl()
		{
			InitializeComponent();
			_tabControl.Font = SystemFonts.MenuFont;
		}

		public void SetTexts(Dictionary<string, string> texts)
		{
			string previouslySelected = string.Empty;
			if(_tabControl.SelectedTab!=null)
			{
				previouslySelected = _tabControl.SelectedTab.Tag as string;
			}
			_tabControl.TabPages.Clear();
			foreach (var text in texts)
			{
				var tab = new TabPage(text.Key);
				tab.Tag = text.Key;
				var textBox = new TextBox();
				textBox.ReadOnly = true;
				textBox.Text = text.Value;
				textBox.Dock = DockStyle.Fill;
				textBox.WordWrap = true;
				textBox.Multiline = true;
				textBox.Font = SystemFonts.MessageBoxFont;
				tab.Controls.Add(textBox);
				_tabControl.TabPages.Add(tab);
				if(text.Key == previouslySelected)
				{
					_tabControl.SelectedTab = tab;
				}
			}
		}
	}
}
