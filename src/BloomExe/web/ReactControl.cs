using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

            var bundleToViteModulePathMap = new Dictionary<string, string>
            {
                { "collectionsTabPaneBundle", "/collectionsTab/CollectionsTabPane.entry.tsx" },
                { "bookMakingSettingsBundle", "/collection/bookMakingSettingsControl.entry.tsx" },
                {
                    "autoUpdateSoftwareDlgBundle",
                    "/react_components/AutoUpdateSoftwareDialog.entry.tsx"
                },
                {
                    "copyrightAndLicenseBundle",
                    "/bookEdit/copyrightAndLicense/CopyrightAndLicenseDialog.entry.tsx"
                },
                {
                    "createTeamCollectionDialogBundle",
                    "/teamCollection/CreateTeamCollection.entry.tsx"
                },
                { "editTopBarControlsBundle", "/bookEdit/topbar/editTopBarControls.entry.tsx" },
                { "duplicateManyDlgBundle", "/bookEdit/duplicateManyDialog.entry.tsx" },
                {
                    "joinTeamCollectionDialogBundle",
                    "/teamCollection/JoinTeamCollectionDialog.entry.tsx"
                },
                { "languageChooserBundle", "/collection/LanguageChooserDialog.entry.tsx" },
                { "messageBoxBundle", "/utils/BloomMessageBox.entry.tsx" },
                {
                    "newCollectionLanguageChooserBundle",
                    "/collection/NewCollectionLanguageChooser.entry.tsx"
                },
                { "problemReportBundle", "/problemDialog/ProblemDialog.entry.tsx" },
                { "progressDialogBundle", "/react_components/Progress/ProgressDialog.entry.tsx" },
                { "publishTabPaneBundle", "/publish/PublishTab/PublishTabPane.entry.tsx" },
                { "registrationDialogBundle", "/react_components/registrationDialog.entry.tsx" },
                { "subscriptionSettingsBundle", "/collection/subscriptionSettingsTab.entry.tsx" },
                {
                    "teamCollectionSettingsBundle",
                    "/teamCollection/TeamCollectionSettingsPanel.entry.tsx"
                },
                {
                    "accessibilityCheckBundle",
                    "/publish/accessibilityCheck/accessibilityCheckScreen.entry.tsx"
                },
            };
            // Should we load relevant assets from the Vite Dev server?
            // To save time, only consider it if this is a dev build.
            // This also guards against trying to load assets from the vite server
            // if a developer runs some other version. Though, it could still be a
            // problem if a dev is trying to run dev builds of two versions at once.
            var useViteDev =
                ApplicationUpdateSupport.IsDev
                && bundleToViteModulePathMap.ContainsKey(_javascriptBundleName);
            var viteModulePath = useViteDev
                ? bundleToViteModulePathMap[_javascriptBundleName]
                : null;
            // If still an option, see if localhost:5173 is running. This is quite slow when it is not.
            // The original version used 400ms, which meant a 1200ms delay; but if it's going to succeed,
            // it typically does so in 2ms. I compromised on 40.
            useViteDev &= IsLocalPortOpen(5173, 40);
            var body =
                $@"
                <body style='margin:0; height:100%; display: flex; flex: 1; flex-direction: column; background-color:{backColor};{overflowY}'>
                    <div id='reactRoot' style='height:100%'>
                    <div class='spinner-container' style='position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%);'>
                        <svg class='spinner' width='40' height='40' viewBox='0 0 40 40' style='animation: spin 1s linear infinite;'>
                            <circle cx='20' cy='20' r='16' fill='none' stroke='#808080' stroke-width='4' stroke-dasharray='75.4 25.13' stroke-linecap='round'/>
                        </svg>
                    </div>
                    <style>
                        @keyframes spin {{
                            to {{ transform: rotate(360deg); }}
                        }}
                    </style>
                    </div>
                </body>";

            if (viteModulePath != null && useViteDev)
            {
                RobustFile.WriteAllText(
                    tempFile.Path,
                    $@"<!DOCTYPE html>
                <html style='height:100%'>
                <head>
                    <title>ReactControl (Vite {_javascriptBundleName})</title>
                    <meta charset='UTF-8' />
                    <script>
                        // Provide no-op React Fast Refresh globals so dev transforms don't crash in WebView.
                        window.__vite_plugin_react_preamble_installed__ = true;
                        window.$RefreshSig$ = window.$RefreshSig$ || (function () {{ return function (type) {{ return type; }}; }});
                        window.$RefreshReg$ = window.$RefreshReg$ || function () {{}};
                        window.__reactControlProps__ = {props};
                        // Shim Node-style globals for browser-only environment
                        if (typeof window.global === 'undefined') window.global = window;
                        if (typeof window.globalThis === 'undefined') window.globalThis = window;
                    </script>
                    <script type='importmap'>
                    {{
                      ""imports"": {{
                        ""jquery"": ""http://localhost:5173/@id/jquery"",
                        ""xregexp"": ""http://localhost:5173/@id/xregexp"",
                        ""underscore"": ""http://localhost:5173/@id/underscore""
                      }}
                    }}
                    </script>

                    <!-- Vite HMR client -->
                    <script type='module' src='http://localhost:5173/@vite/client'></script>

                    <!-- Wait for Vite to finish optimizing deps, then import and expose globals, then load the entry. -->
                    <script type='module'>



                        async function main() {{
                            try {{

                                // Import via bare specifiers (resolved by import map) and assign globals expected by legacy libs.
                                const jQuery = (await import('jquery')).default;
                                // Some builds export default, others export the function itself. Normalize.
                                const jq = jQuery && jQuery.fn ? jQuery : (jQuery && jQuery.default ? jQuery.default : jQuery);
                                window.$ = jq;
                                window.jQuery = jq;
                                console.log('jQuery ready via Vite prebundle');

                                const XRegExp = (await import('xregexp')).default || (await import('xregexp'));
                                window.XRegExp = XRegExp.default || XRegExp;
                                console.log('XRegExp ready via Vite prebundle');

                                const _mod = await import('underscore');
                                window._ = _mod.default || _mod;
                                console.log('Underscore ready via Vite prebundle');

                                // Finally, load the app entry.
                                await import('http://localhost:5173{viteModulePath}');
                            }} catch (e) {{
                                console.error('Failed to initialize Vite dev page:', e);
                            }}
                        }}

                        main();
                    </script>
                </head>
                {body}
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
                    <script src = '/{bundleNameWithExtension}'  type='module'></script>
					<script>
						window.onload = () => {{
							const rootDiv = document.getElementById('reactRoot');
							window.wireUpRootComponentFromWinforms(rootDiv, {props});
						}};
					</script>
				</head>
				{body}
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
                    var asyncResult = client.BeginConnect(host, port, null, null);
                    var ok = asyncResult.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (!ok)
                        return false;
                    client.EndConnect(asyncResult);
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
