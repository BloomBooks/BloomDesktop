using System.Globalization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using SIL.Reporting;

namespace Bloom
{
    /// <summary>
    /// A WebView2 control that guards against a WinForms/.NET crash (BL-16536) that happens when the
    /// user switches to a keyboard whose input language reports a BCP-47 tag that .NET does not
    /// recognize as a culture. For example, the Keyman IPA keyboard reports "und-Latn" (undetermined
    /// language, Latin script). When the input language changes, Windows sends WM_INPUTLANGCHANGE to
    /// the focused window (which, while the user is typing, is this WebView2 control). WinForms' handler
    /// for that message builds a CultureInfo from the keyboard's tag and throws
    /// CultureNotFoundException. Because that runs inside WndProc during message dispatch, it is
    /// otherwise a fatal, unhandled exception on the UI thread.
    ///
    /// Bloom does all its text editing inside WebView2, which tracks its own input language
    /// independently of WinForms, so we can safely ignore WinForms' inability to represent the
    /// keyboard and just perform the default window processing.
    /// </summary>
    public class BloomWebView2 : WebView2
    {
        private const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const int WM_INPUTLANGCHANGE = 0x0051;

        /// <summary>
        /// Intercepts the input-language-change messages so that an unrecognized keyboard culture
        /// cannot crash Bloom. See the class comment for the full explanation (BL-16536).
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUTLANGCHANGE || m.Msg == WM_INPUTLANGCHANGEREQUEST)
            {
                try
                {
                    base.WndProc(ref m);
                }
                catch (CultureNotFoundException e)
                {
                    Logger.WriteMinorEvent(
                        "Ignoring unsupported input-language culture from keyboard (BL-16536): "
                            + e.Message
                    );
                    // WinForms' own handler for these messages ends by calling DefWndProc; since its
                    // handler threw before reaching that point, we do the default processing here so
                    // Windows still handles the language change normally.
                    DefWndProc(ref m);
                }
                return;
            }
            base.WndProc(ref m);
        }
    }
}
