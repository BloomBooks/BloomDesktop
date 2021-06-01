using System;
using System.Windows.Forms;
using L10NSharp;

namespace Bloom.Collection
{
	public partial class ScriptSettingsDialog : Form
	{
		public ScriptSettingsDialog()
		{
			InitializeComponent();
			SetLineHeightList();
			LoadFontSizeCombo();
			_languageNameLabel.UseMnemonic = false;
		}

		private void SetLineHeightList()
		{
			// build the list of possible line heights
			var defaultText = LocalizationManager.GetDynamicString("Bloom", "ScriptSettingsDialog.DefaultLineSpacing",
				"Default line spacing");

			_lineSpacingCombo.Items.Clear();
			_lineSpacingCombo.Items.Add(defaultText);

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

			// size the combo box
			using (var g = _lineSpacingCombo.CreateGraphics())
			{
				var w = TextRenderer.MeasureText(g, defaultText, Font);
				_lineSpacingCombo.Width = w.Width + 20;
			}
		}

		public string LanguageName
		{
			set { _languageNameLabel.Text = value; }
		}

		public bool LanguageRightToLeft
		{
			get { return _rtlLanguageCheckBox.Checked; }
			set { _rtlLanguageCheckBox.Checked = value; }
		}

		// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5761.
		public bool BreakLinesOnlyAtSpaces
		{
			get { return _lineBreakCheckBox.Checked; }
			set { _lineBreakCheckBox.Checked = value; }
		}

		public decimal LanguageLineSpacing
		{
			get
			{
				if (!_lineSpacingCombo.Enabled) return 0;
				return _lineSpacingCombo.SelectedIndex < 1 ? 0 : Convert.ToDecimal(_lineSpacingCombo.Text);
			}
			set
			{
				if (value < 1)
				{
					_lineSpacingCombo.SelectedIndex = 0;
				}
				else
				{
					for (var i = 1; i < _lineSpacingCombo.Items.Count; i++)
					{
						if (value == Convert.ToDecimal(_lineSpacingCombo.Items[i].ToString()))
						{
							_lineSpacingCombo.SelectedIndex = i;
							_tallerLinesCheckBox.Checked = true;
							break;
						}
					}
				}

				_lineSpacingCombo.Enabled = _tallerLinesCheckBox.Checked;
			}
		}

		private void _tallerLinesCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			_lineSpacingCombo.Enabled = _tallerLinesCheckBox.Checked;
		}


		/// <summary>
		/// Here 0 means "use the default".
		/// </summary>
		public int UIFontSize
		{
			get
			{
				return _uiFontSizeCombo.SelectedIndex < 1 ? 0 : Convert.ToInt16(_uiFontSizeCombo.Text);
			}
			set
			{
				_uiFontSizeCombo.SelectedItem = null;
				var sizeAsString = value.ToString();
				foreach (var item in _uiFontSizeCombo.Items)
				{
					if (item.ToString() == sizeAsString)
					{
						_uiFontSizeCombo.SelectedItem = item;
					}
				}

				if (_uiFontSizeCombo.SelectedItem == null)
				{
					// if we couldn't find a match, use the default, which is some text at the beginning
					_uiFontSizeCombo.SelectedIndex = 0; 
				}
			}
		}
		private void LoadFontSizeCombo()
		{
			var defaultText = LocalizationManager.GetDynamicString("Bloom", "ScriptSettingsDialog.DefaultSize",
				"Default size");

			_uiFontSizeCombo.Items.Clear();
			_uiFontSizeCombo.Items.Add(defaultText);
			_uiFontSizeCombo.SelectedIndex = 0;
			var fontSizes = new[] { 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26 };
			foreach (var i in fontSizes)
			{
				_uiFontSizeCombo.Items.Add(i.ToString());
			}
			// Copied this from the other combobox... maybe be something needed for Mono?
			// Make the combo box just wide enough to show its content.
			using (var g = _uiFontSizeCombo.CreateGraphics())
			{
				var w = TextRenderer.MeasureText(g, defaultText, Font);
				_uiFontSizeCombo.Width = w.Width + 40; // allow room for dropdown icon and text margins
			}
		}
	}
}
