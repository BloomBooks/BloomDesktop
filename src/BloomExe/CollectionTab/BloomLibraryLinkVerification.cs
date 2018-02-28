using System;
using System.Drawing;
using System.Windows.Forms;
using L10NSharp;

namespace Bloom.CollectionTab
{
	public partial class BloomLibraryLinkVerification : Form
	{
		public BloomLibraryLinkVerification()
		{
			InitializeComponent();
		}

		private void Initialize()
		{
			// Set localized caption
			Text = LocalizationManager.GetString("CollectionTab.BloomLibraryLinkVerificationCaption", "Source Collection", "get this clicking on BloomLibrary.org link in source collection");

			// Set localized message
			var msg = "Note: The Bloom Library contains shell books, whereas ";
			msg += "your current collection is a \"source collection\", meaning it is for making shell books, ";
			msg += "not for translating them into the local language.";
			var part1 = LocalizationManager.GetString("CollectionTab.BloomLibraryLinkVerification.Part1", msg, "get this clicking on BloomLibrary.org link in source collection");
			msg = "When you've made a shell, you can then upload it to the Bloom Library from the Publish tab.";
			var part2 = LocalizationManager.GetString("CollectionTab.BloomLibraryLinkVerification.Part2", msg, "get this clicking on BloomLibrary.org link in source collection");
			_message.Text = part1 + Environment.NewLine + Environment.NewLine + part2;

			// Set information icon
			// Review: Could use a larger image here...
			_infoIcon.Image = SystemIcons.Information.ToBitmap();
		}

		internal DialogResult GetVerification(IWin32Window owner)
		{
			Initialize();
			return ShowDialog(owner);
		}
	}
}
