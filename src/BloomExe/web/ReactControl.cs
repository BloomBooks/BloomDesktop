using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SIL.IO;
using SIL.Windows.Forms.Extensions;

namespace Bloom.web
{
	/// <summary>
	/// Hosts a Web Browser rooted by the named React component
	/// </summary>
	public partial class ReactControl : UserControl
	{
		private string _javascriptBundleName;
		private string _reactComponentName;
		public ReactControl()
		{
			InitializeComponent();
		}

		[Browsable(true), Category("Setup")]
		public string JavascriptBundleName
		{
			get { return _javascriptBundleName; }
			set { _javascriptBundleName = value; }
		}

		[Browsable(true), Category("Setup")]
		public string ReactComponentName
		{
			get { return _reactComponentName; }
			set { _reactComponentName = value; }
		}

		private void ReactControl_Load(object sender, System.EventArgs e)
		{
			if (this.DesignModeAtAll())
				return;

			var tempFile = TempFile.WithExtension("htm");
			RobustFile.WriteAllText(tempFile.Path, $@"<!DOCTYPE html>
				<html>
				<head>
					<meta charset = 'UTF-8' />
					<script src = '/commonBundle.js' ></script>
					<script src = '/wireUpBundle.js' ></script>
					<script src = '/{_javascriptBundleName}'></script>
					<script>
						window.onload = () => {{
							const rootDiv = document.getElementById('reactRoot');
							window.wireUpReact(rootDiv,'{_reactComponentName}');
						}};
					</script>
				</head>
				<body>
					<div id='reactRoot'>Component should replace this</div >
				</body>
				</html>");

			var url = tempFile.Path.ToLocalhost();

			// The Size setting is needed on Linux to keep the browser from coming up as a small
			// rectangle in the upper left corner...
			var browser = new Browser
				{ Dock = DockStyle.Fill, Location = new Point(3, 3), Size = new Size(this.Width - 6, this.Height - 6) };
			browser.BackColor = Color.White;
			
			var dummy = browser.Handle; // gets the WebBrowser created

			// If the control gets added before it has navigated somewhere,
			// it shows as solid black, despite setting the BackColor to white.
			// So just don't show it at all until it contains what we want to see.
			browser.WebBrowser.DocumentCompleted += (unused, args) =>
			{
				this.Controls.Add(browser);
				//browser.Focus();
			};
			browser.Navigate(url, cleanupFileAfterNavigating: false /* TODO: I was getting errors*/);
		}
	}
}
