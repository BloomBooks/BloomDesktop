using System;
using System.Windows.Forms;
using SIL.PlatformUtilities;
using Gecko;
using Gecko.DOM;

namespace Bloom.Publish
{
	/// <summary>
	/// This configurable class provides a C# wrapper for several of the publish option 
	/// panels that appear in the Publish tab, for which the UI is an html component
	/// </summary>
	public partial class HtmlPublishPanel : UserControl
	{
		private Browser _browser;

		public HtmlPublishPanel(string pathToHtmlFile)
		{
			InitializeComponent();

			_browser = new Browser();
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);
			// Has to be in front of the panel docked top for Fill to work.
			_browser.BringToFront();
			_browser.Navigate(pathToHtmlFile.ToLocalhost() + GetUrlParams(), false);
			_browser.OnBrowserClick += HandleExternalLinkClick;

			VisibleChanged += OnVisibleChanged;
		}

		public Browser Browser => _browser;

		private string GetUrlParams()
		{
			return $"?isLinux={Platform.IsLinux}";
		}

		private void OnVisibleChanged(object sender, EventArgs eventArgs)
		{
			if (!Visible)
			{
				Deactivate();
			}
		}

		/// <summary>
		/// Detect clicks on anchor elements and handle them by passing the href to the default system browser.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-7569.
		/// Note that Readium handles these clicks internally and we never get to see them.  The relevant Readium
		/// code is apparently in readium-js-viewer/readium-js/readium-shared-js/js/views/internal_links_support.js
		/// in the function this.processLinkElements.
		/// </remarks>
		private void HandleExternalLinkClick(object sender, EventArgs e)
		{
			var ge = e as DomEventArgs;
			if (ge == null || ge.Target == null)
				return;
			GeckoHtmlElement target;
			try
			{
				target = (GeckoHtmlElement)ge.Target.CastToGeckoElement();
			}
			catch (InvalidCastException)
			{
				return;
			}
			var anchor = target as GeckoAnchorElement;
			if (anchor == null)
				anchor = target.Parent as GeckoAnchorElement;	// Might be a span inside an anchor
			if (anchor != null && !String.IsNullOrEmpty(anchor.Href) &&
				// Handle only http(s) and mailto protocols.
				(anchor.Href.ToLowerInvariant().StartsWith("http") || anchor.Href.ToLowerInvariant().StartsWith("mailto")) &&
				// Don't try to handle localhost Bloom requests.
				!anchor.Href.ToLowerInvariant().StartsWith(Bloom.Api.BloomServer.ServerUrlWithBloomPrefixEndingInSlash))
			{
				SIL.Program.Process.SafeStart(anchor.Href);
				ge.Handled = true;
			}
			// All other clicks get normal processing...
		}

		private void Deactivate()
		{
			// This is important so the react stuff can do its cleanup
			_browser.WebBrowser.Navigate("about:blank");
		}

	}
}
