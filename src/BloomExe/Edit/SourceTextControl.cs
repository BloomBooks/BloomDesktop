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
		private string _previouslySelectedLangCode;

		public SourceTextControl()
		{
			InitializeComponent();
			_tabControl.Font = SystemFonts.MenuFont;
			_tabControl.TabPages.Clear();
		}

		public void SetTexts(Dictionary<string, string> texts)
		{
			//_tabControl.TabPages.Clear();//this (starting fresh each time), though simple, lead to all maner of focus problems

			foreach (var pair in texts)
			{
				TabPage tab = GetOrCreateTab(pair.Key);
				((TextBox)tab.Tag).Text = pair.Value;
			}
		}

		private TabPage GetOrCreateTab(string languageCode)
		{
			if (_tabControl.TabPages.ContainsKey(languageCode))
				return _tabControl.TabPages[languageCode];

			_tabControl.TabPages.Add(languageCode, languageCode);
			var tab = _tabControl.TabPages[languageCode];

			var textBox = new TextBox
							  {
								  ReadOnly = true,
								  Dock = DockStyle.Fill,
								  WordWrap = true,
								  Multiline = true,
								  Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 12)
							  };

			tab.Controls.Add(textBox);
			tab.Tag = textBox;
			return tab;
		}

		public void ClearTextContents()
		{

			foreach (TabPage tabPage in _tabControl.TabPages)
			{
				((TextBox) tabPage.Tag).Text = string.Empty;
			}
		}
	}
}
