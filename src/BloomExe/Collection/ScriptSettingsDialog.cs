﻿using System;
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

			_lineSpacingCombo.Items.Add("2.5");
			_lineSpacingCombo.Items.Add("3.0");

			// size the combo box
			using (var g = _lineSpacingCombo.CreateGraphics())
			{
				var w = g.MeasureString(defaultText, Font);
				_lineSpacingCombo.Width = (int)w.Width + 20;
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
	}
}
