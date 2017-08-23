using System;
using System.Diagnostics;
using System.Windows.Forms;
using Gecko;
using SIL.PlatformUtilities;

namespace Bloom.Publish.Android
{
	/// <summary>
	/// This class implements the panel that appears in the Publish tab when the Android button is selected.
	/// </summary>
	public partial class AndroidView : UserControl
	{
		private Browser _browser;

		public AndroidView(NavigationIsolator isolator)
		{
			InitializeComponent();

			_browser = new Browser();
			_browser.Isolator = isolator;
			_browser.Dock = DockStyle.Fill;
			_browser.OnBrowserClick += OnBrowserClick;
			Controls.Add(_browser);
			// Has to be in front of the panel docked top for Fill to work.
			_browser.BringToFront();
			BloomFileLocator.GetBrowserFile("gulpfile.js");
			var path = BloomFileLocator.GetBrowserFile("publish","android","androidPublishUI.html");
			_browser.Navigate(path.ToLocalhost() + GetUrlParams(), false);

			VisibleChanged += OnVisibleChanged;
		}

		/// <summary>
		/// if a link has target "_blank", which normally means
		/// "open in a new tab/window", we take that to mean "open in external browser"
		/// </summary>
		private void OnBrowserClick(object sender, EventArgs e)
		{
			var ge = e as DomEventArgs;
			var target = (GeckoHtmlElement)ge.Target.CastToGeckoElement();
			var anchor = target as Gecko.DOM.GeckoAnchorElement;
			// it's going to be null when they click on other things, an also this will get
			// called first on the label itself, then later on the parent anchor.
			if (anchor != null)
			{
				// Setting the target doesn't yet buy us anything.
				// We don't yet have any links that we want to just change the current page in the embedded browser.
				// But it strikes me as proper form, as it is what you'd do in normal web page.
				if (anchor.Target == "_blank")
				{
					//open it in their browser
					Process.Start(anchor.Href);
					ge.Handled = true;
				}
			}
		}

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

		public void Deactivate()
		{
			// This is important so the react stuff can do its cleanup
			_browser.WebBrowser.Navigate("about:blank");
		}
	}
}
