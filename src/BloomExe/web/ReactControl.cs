using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Utils;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Windows.Forms.Extensions;

namespace Bloom.web
{
    /// <summary>
    /// Hosts a Web Browser rooted by the named React component
    /// </summary>
    // Possible future enhancement: wouldn't be hard to add the ability to pass
    // an object as the props for the control. This would be helpful in
    // cases where supplying the props from the parent c# would mean we didn't
    // have to create an API for this component. However, since we eventually
    // want to get rid of WinForms entirely, it's not yet clear to me if we
    // would eventually have to create that api anyways? (Could be cases where
    // the eventual JS/TS parent control would have the values needed for the props
    // without an API existing for that same data. As I say, unclear).

    public partial class ReactControl : UserControl
    {
        private string _javascriptBundleName;

        // props to provide to the react component
        public object Props;

        public static ReactControl Create(string _javascriptBundleName)
        {
            return new ReactControl() { JavascriptBundleName = _javascriptBundleName };
        }

        /* Ideally this would be private but I don't know how to do that without messing up winform Designer code that uses it */
        internal ReactControl()
        {
            InitializeComponent();
            BackColor = Color.White; // we use a different color in design mode
        }

        [Browsable(true), Category("Setup")]
        public string JavascriptBundleName
        {
            get { return _javascriptBundleName; }
            set { _javascriptBundleName = value; }
        }

        public bool UseEditContextMenu;
        public bool HideVerticalOverflow;
        public event EventHandler OnBrowserClick;

        private Browser _browser;

        private void ReactControl_Load(object sender, System.EventArgs e)
        {
            if (this.DesignModeAtAll())
            {
                _settingsDisplay.Visible = true;
                _settingsDisplay.Text =
                    $"ReactControl{Environment.NewLine}{Environment.NewLine}Javascript Bundle: {_javascriptBundleName}{Environment.NewLine}{Environment.NewLine}Remember to call WireUpForWinforms() from the bundle.";
                return;
            }

            _settingsDisplay.Visible = false;

            var tempFile = MakeTempFile();

            // The Size setting is needed on Linux to keep the browser from coming up as a small
            // rectangle in the upper left corner...
            //_browser = new GeckoFxBrowser
            _browser = BrowserMaker.MakeBrowser();
            var browserControl = _browser;

            browserControl.Dock = DockStyle.Fill;
            browserControl.Location = new Point(0, 0);
            browserControl.Size = new Size(Width, Height);

            // These three lines eliminate a border that was showing up in the Copy/Paste control in the Edit Tab toolbar, BL-15024
            browserControl.BackColor = this.BackColor;
            browserControl.Margin = new Padding(0);
            browserControl.Padding = new Padding(0);

            // currently this is used only in ReactDialog. E.g., "Report a problem".
            if (UseEditContextMenu)
                _browser.ContextMenuProvider = (adder) =>
                {
                    adder.Add(
                        L10NSharp.LocalizationManager.GetString("Common.Copy", "Copy"),
                        (s1, e1) =>
                        {
                            _browser.CopySelection();
                        }
                    );
                    adder.Add(
                        L10NSharp.LocalizationManager.GetString("Common.SelectAll", "Select all"),
                        (s1, e1) =>
                        {
                            _browser.SelectAll();
                        }
                    );
                    return true;
                };

            _browser.EnsureHandleCreated();

            _browser.OnBrowserClick += (s, args) =>
            {
                OnBrowserClick?.Invoke(this, args);
            };

            // If the control gets added before it has navigated somewhere,
            // it shows as solid black, despite setting the BackColor to white.
            // So just don't show it at all until it contains what we want to see.
            // Note, this means any alerts that come up in the initialization code will freeze things,
            // since the user can't see them to respond. Don't use alerts in the initialization code!
            _browser.DocumentCompleted += (unused, args) =>
            {
                if (this.IsDisposed)
                    return;
                Controls.Add((UserControl)_browser); //review this cast

                // This allows us to bring up a react control/dialog with focus already set to a specific element.
                // For example, for BloomMessageBox, we set the Cancel button to have focus so the user
                // can hit the Enter key to close the dialog.
                // The first attempt to allow this behavior called root.click() in WireUpWinform.tsx
                // which then caused Browser.OnBrowser_DomClick to fire which called
                // WebBrowserFocus.Activate(). But that was causing the Shell to lose focus.
                // The problem was that Browser didn't have a Parent at that point.
                // By making the Activate call here, we seem to solve both issues. See BL-11092.
                _browser.ActivateFocussed();
            };
            _browser.NavigateToTempFileThenRemoveIt(tempFile.Path);
        }

        // If given the localization changed event, the control will automatically reload
        // when the event is raised.
        public void SetLocalizationChangedEvent(LocalizationChangedEvent localizationChangedEvent)
        {
            localizationChangedEvent.Subscribe(unused =>
            {
                Reload();
            });
        }

        public void Reload()
        {
            if (_browser == null)
                return;
            var tempFile = MakeTempFile();
            _browser.NavigateToTempFileThenRemoveIt(tempFile.Path);
        }

        private TempFile MakeTempFile()
        {
            var tempFile = TempFile.WithExtension("htm");
            tempFile.Detach(); // the browser control will clean it up

            var props = Props == null ? "{}" : JsonConvert.SerializeObject(Props);

            if (_javascriptBundleName == null)
            {
                throw new ArgumentNullException("React Control needs a _javascriptBundleName");
            }

            var bundleNameWithExtension = _javascriptBundleName;
            if (!bundleNameWithExtension.EndsWith(".js"))
            {
                bundleNameWithExtension += ".js";
            }

            // We insert this as the initial background color of the HTML element
            // to prevent a flash of white while the React is rendering.
            var backColor = MiscUtils.ColorToHtmlCode(BackColor);

            var overflowY = HideVerticalOverflow ? " overflow-y: hidden;" : "";

            // Special development mode: if the bundle name matches one of certain tab panes, load via Vite dev server instead of webpack bundle.
            // This allows rapid iteration without running the whole webpack build. We only do this if a dev server is assumed to be running.

            // make a mapping from bundle names to vite module paths.
            var modulePathMap = new Dictionary<string, string>
            {
                { "collectionsTabPaneBundle", "/collectionsTab/CollectionsTabPane.tsx" },
                { "publishTabPaneBundle", "/publish/PublishTab/PublishTabPane.tsx" },
            };
            var modulePath = modulePathMap.ContainsKey(_javascriptBundleName)
                ? modulePathMap[_javascriptBundleName]
                : null;
            // see if localhost:5173 is running
            var viteDevServerRunning = IsLocalPortOpen(5173, 400);
            if (modulePath != null && viteDevServerRunning)
            {
                // Map the bundle name to its TSX module path relative to BloomBrowserUI root used by Vite.
                // Expect dev server at http://localhost:5173.
                var moduleImportPath =
                    _javascriptBundleName == "collectionsTabPaneBundle"
                        ? "/collectionsTab/CollectionsTabPane.entry.tsx"
                        : "/publish/PublishTab/PublishTabPane.entry.tsx";

                RobustFile.WriteAllText(
                    tempFile.Path,
                    $@"<!DOCTYPE html>
                <html style='height:100%'>
                <head>
                    <title>ReactControl (Vite {_javascriptBundleName})</title>
                    <meta charset='UTF-8' />
                    <script src='https://cdn.jsdelivr.net/npm/jquery@3.7.1/dist/jquery.min.js'></script>
                    <script>
                        // Ensure legacy plugins see global jQuery in WebView2
                        window.$ = window.$ || window.jQuery;
                        window.jQuery = window.jQuery || window.$;
                    </script>
                    <script>
                        // Provide no-op React Fast Refresh globals so dev transforms don't crash in WebView.
                        window.__vite_plugin_react_preamble_installed__ = true;
                        window.$RefreshSig$ = window.$RefreshSig$ || (function () {{ return function (type) {{ return type; }}; }});
                        window.$RefreshReg$ = window.$RefreshReg$ || function () {{}};
                    </script>
                    <script>
                        window.__reactControlProps__ = {props};
                    </script>
                    <script type='module' src='http://localhost:5173/@vite/client'></script>
                    <script type='module' src='http://localhost:5173{moduleImportPath}'></script>
                </head>
                <body style='margin:0; height:100%; display:flex; flex:1; flex-direction:column; background-color:{backColor};{overflowY}'>
                    <div id='reactRoot' style='height:100%'>Loading Vite module {moduleImportPath}...</div>
                </body>
                </html>"
                );
            }
            else
            {
                // The 'body' height: auto rule keeps a winforms tab that only contains a ReactControl
                // from unnecessary scrolling.
                RobustFile.WriteAllText(
                    tempFile.Path,
                    $@"<!DOCTYPE html>
				<html style='height:100%'>
				<head>
					<title>ReactControl ({_javascriptBundleName})</title>
					<meta charset = 'UTF-8' />
					<script src = '/commonBundle.js' ></script>
                    <script src = '/{bundleNameWithExtension}'></script>
					<script>
						window.onload = () => {{
							const rootDiv = document.getElementById('reactRoot');
							window.wireUpRootComponentFromWinforms(rootDiv, {props});
						}};
					</script>
				</head>
				<body style='margin:0; height:100%; display: flex; flex: 1; flex-direction: column; background-color:{backColor};{overflowY}'>
					<div id='reactRoot' style='height:100%'>Javascript should have replaced this. Make sure that the javascript bundle '{bundleNameWithExtension}' includes a single call to WireUpForWinforms()</div>
				</body>
				</html>"
                );
            }
            return tempFile;
        }

        private static bool IsLocalPortOpen(int port, int timeoutMs = 400)
        {
            try
            {
                // Try IPv6 localhost first (common on Windows), then IPv4.
                if (TryConnect("::1", port, timeoutMs))
                    return true;
                if (TryConnect("127.0.0.1", port, timeoutMs))
                    return true;
                // As a fallback, try host name which may resolve differently.
                if (TryConnect("localhost", port, timeoutMs))
                    return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConnect(string host, int port, int timeoutMs)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var ar = client.BeginConnect(host, port, null, null);
                    var ok = ar.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (!ok)
                        return false;
                    client.EndConnect(ar);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
