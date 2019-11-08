using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Collection;
using L10NSharp;

namespace Bloom.MiscUI
{
	public partial class LanguageFontDetails : UserControl
	{
		public LanguageFontDetails()
		{
			InitializeComponent();
			LoadFontCombo();
			LoadLineHeightCombo();
		}

		private void LoadFontCombo()
		{
			// Display the fonts in sorted order.
			var fontNames = new List<string>();
			fontNames.AddRange(Browser.NamesOfFontsThatBrowserCanRender());
			fontNames.Sort();
			var defaultFont = LanguageSpec.GetDefaultFontName();
			foreach (var font in fontNames)
			{
				_fontCombo.Items.Add(font);
				if (font == defaultFont)
					_fontCombo.SelectedItem = font;
			}

			// Make the font combobox wide enough to display the longest value.
			int width = _fontCombo.DropDownWidth;
			using (Graphics g = _fontCombo.CreateGraphics())
			{
				Font font = _fontCombo.Font;
				int vertScrollBarWidth = (_fontCombo.Items.Count > _fontCombo.MaxDropDownItems) ? SystemInformation.VerticalScrollBarWidth : 0;

				width = (from string s in _fontCombo.Items select TextRenderer.MeasureText(g, s, font).Width).Concat(new[] { width }).Max() + vertScrollBarWidth;
			}
			_fontCombo.DropDownWidth = width;
		}

		private void LoadLineHeightCombo()
		{
			// build the list of possible line heights
			var defaultText = LocalizationManager.GetDynamicString("Bloom", "ScriptSettingsDialog.DefaultLineSpacing", "Default line spacing");
			_lineSpacingCombo.Items.Clear();
			_lineSpacingCombo.Items.Add(defaultText);
			_lineSpacingCombo.SelectedIndex = 0;
			// We display the font size choices to an accuracy of 0.1 in the current culture.
			var fontSize = 1.0;
			while (fontSize < 2.1)
			{
				_lineSpacingCombo.Items.Add(fontSize.ToString("0.0"));
				fontSize += 0.1;
			}
			fontSize = 2.5;
			_lineSpacingCombo.Items.Add(fontSize.ToString("0.0"));
			fontSize = 3.0;
			_lineSpacingCombo.Items.Add(fontSize.ToString("0.0"));

			// Make the combo box just wide enough to show its content.
			using (var g = _lineSpacingCombo.CreateGraphics())
			{
				var w = TextRenderer.MeasureText(g, defaultText, Font);
				_lineSpacingCombo.Width = w.Width + 40;	// allow room for dropdown icon and text margins
			}
			_lineSpacingCombo.Enabled = false;
		}

		public string SelectedFont
		{
			get { return _fontCombo.Text; }
		}

		public bool RightToLeft
		{
			get { return _rightToLeftCheck.Checked; }
		}

		public bool ExtraLineHeight
		{
			get { return _tallerLinesCheck.Checked; }
		}

		public string LineHeight
		{
			get { return _lineSpacingCombo.Text; }
		}

		private void _tallerLinesCheck_CheckedChanged(object sender, System.EventArgs e)
		{
			_lineSpacingCombo.Enabled = _tallerLinesCheck.Checked;
		}
	}
}
