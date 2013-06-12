using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Properties;

namespace Bloom.MiscUI
{
	public partial class TipDialog : Form
	{
		public string DialogTitle
		{
			get { return Text; }
			set { Text = value; }
		}

		public string Message
		{
			get { return _message.Text; }
			set { _message.Text = value; }
		}

		public Image Icon
		{
			get { return _icon.Image; }
			set { _icon.Image = value; }
		}

		public static void Show(string message)
		{
			using (var d = new TipDialog(message, "Tip", SystemIcons.Information.ToBitmap()))
				d.ShowDialog();
		}

		private TipDialog()
		{
			InitializeComponent();
			_message.Font = SystemFonts.MessageBoxFont;
			_message.BackColor = BackColor;
			_message.ForeColor = ForeColor;
			_icon.Image = SystemIcons.Warning.ToBitmap();
			base.Icon = SystemIcons.Warning;
			dontShowThisAgainButton1.ResetDontShowMemory(Settings.Default);
			dontShowThisAgainButton1.CloseIfShouldNotShow(Settings.Default, Message);
		}

		/// <summary>
		/// Use this one if you need to customize the dialog, e.g. to setup an alternate button
		/// </summary>
		public TipDialog(string message, string dialogTitle, Image icon) : this()
		{
			if (icon != null)
				_icon.Image = icon;

			Text = dialogTitle;
			_message.Text = message;
		}

		private void _acceptButton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.OK;
			Close();
		}

		private void HandleMessageTextChanged(object sender, EventArgs e)
		{
			AdjustHeights();
		}

		private void AdjustHeights()
		{
			//hack: I don't know why this is needed, but it was chopping off the last line in the case of the following message:
			// "There was a problem connecting to the Internet.\r\nWarning: This machine does not have a live network connection.\r\nConnection attempt failed."
			const int kFudge = 50;
			_message.Height = GetDesiredTextBoxHeight()+kFudge;


			var desiredWindowHeight = tableLayout.Height + Padding.Top +
				Padding.Bottom + (Height - ClientSize.Height);

			var scn = Screen.FromControl(this);
			int maxWindowHeight = scn.WorkingArea.Height - 25;

			if (desiredWindowHeight > maxWindowHeight)
			{
				_message.Height -= (desiredWindowHeight - maxWindowHeight);
				_message.ScrollBars = ScrollBars.Vertical;
			}

			Height = Math.Min(desiredWindowHeight, maxWindowHeight);
		}

		private int GetDesiredTextBoxHeight()
		{
			if (!IsHandleCreated)
				CreateHandle();

			using (var g = _message.CreateGraphics())
			{
				const TextFormatFlags flags = TextFormatFlags.NoClipping | TextFormatFlags.NoPadding |
					TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak;

				return TextRenderer.MeasureText(g, _message.Text, _message.Font,
					new Size(_message.ClientSize.Width, 0), flags).Height;
			}
		}

		private void TipDialog_Load(object sender, EventArgs e)
		{

		}

	}
}