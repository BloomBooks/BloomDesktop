using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SIL.Keyboarding;
using SIL.Windows.Forms.Keyboarding;

namespace Bloom.Keyboarding
{
    /// <summary>
    /// A resolved OS/TSF input method for a language, carrying the HKL we actually post to switch
    /// the input language (libpalaso's composite Id is not an HKL — see the spike findings).
    /// </summary>
    public class OsKeyboardInfo
    {
        /// <summary>The libpalaso keyboard Id (composite string, e.g. "th-TH_Thai Kedmanee_Thai Kedmanee").</summary>
        public string Id;

        /// <summary>The keyboard's locale, e.g. "th-TH".</summary>
        public string Locale;

        /// <summary>The Windows keyboard-layout handle used to switch the input language. IntPtr.Zero if it could not be resolved.</summary>
        public IntPtr Hkl;

        /// <summary>
        /// True when this is (probably) a TSF text-input-processor rather than a plain HKL layout.
        /// The spike could not verify TIP-specific profile selection via an HKL post (no Keyman was
        /// installed on the test machine), so this is informational only and flagged as a residual risk.
        /// </summary>
        public bool IsTip;
    }

    /// <summary>
    /// The queries + activation the keyboard resolver needs from the OS. Extracted as an interface so
    /// the resolver can be unit-tested with a fake (the real implementation touches libpalaso and the
    /// Win32 input stack and must run on the UI thread).
    /// </summary>
    public interface IOsKeyboardService
    {
        /// <summary>Best installed OS keyboard for the language (exact locale beats language-only), or null.</summary>
        OsKeyboardInfo FindBestForLanguage(string tag);

        /// <summary>The installed OS keyboard with the given libpalaso Id, or null if not installed here.</summary>
        OsKeyboardInfo FindById(string libPalasoKeyboardId);

        /// <summary>Switch Bloom's input language to the given keyboard. Returns false if it could not.</summary>
        bool Activate(OsKeyboardInfo keyboard);

        /// <summary>Switch Bloom's input language back to the default (English) layout. Returns false if it could not.</summary>
        bool ActivateDefault();
    }

    /// <summary>
    /// Enumerates installed OS/TSF input methods (via libpalaso) and switches Bloom's input language
    /// to them.
    ///
    /// IMPORTANT — activation mechanism (see Design/Keyboards/spike-os-switching-findings.md):
    /// libpalaso's <c>IKeyboardDefinition.Activate()</c> ends in
    /// <c>ITfInputProcessorProfileMgr.ActivateProfile(..., TF_IPPMF_FORPROCESS)</c>, which is
    /// process-scoped and therefore does NOT reach the separate <c>msedgewebview2.exe</c> process that
    /// actually receives keystrokes in a bloom-editable. The spike proved that the only mechanism that
    /// does affect typing is posting <c>WM_INPUTLANGCHANGEREQUEST</c> to Bloom's foreground top-level
    /// window (which the webview process follows). Posting to the child webview HWND, and our own
    /// TF_IPPMF_FORSESSION activation, both did NOT work. So we use libpalaso only for ENUMERATION and
    /// switch the language by posting to Bloom's main window.
    ///
    /// ALL methods here are UI-thread-only (they read WinForms handles and the Win32 input state);
    /// call them from a <c>handleOnUiThread: true</c> endpoint.
    /// </summary>
    public class OsKeyboards : IOsKeyboardService
    {
        // Windows message + flags for switching the input language of a window.
        private const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private static readonly IntPtr INPUTLANGCHANGE_SYSCHARSET = new IntPtr(0x0001);

        // LoadKeyboardLayout flags. KLF_NOTELLSHELL avoids a shell notification; we only want the HKL.
        private const uint KLF_NOTELLSHELL = 0x0080;

        // The US English layout, used as the neutral "default" when switching back from a non-Latin field.
        private const string kUsEnglishKlid = "00000409";

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr PostMessage(
            IntPtr hWnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam
        );

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

        [DllImport("user32.dll")]
        private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

        /// <summary>
        /// The installed OS keyboards whose language subtag matches <paramref name="tag"/> (region and
        /// script ignored). UI-thread-only. Empty if the controller isn't initialized.
        /// </summary>
        public System.Collections.Generic.IEnumerable<IKeyboardDefinition> GetInstalledKeyboardsForLanguage(
            string tag
        )
        {
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("A language tag is required.", nameof(tag));
            if (!KeyboardController.IsInitialized)
                return Enumerable.Empty<IKeyboardDefinition>();
            var wantLang = LanguageSubtag(tag);
            return Keyboard
                .Controller.AvailableKeyboards.Where(k =>
                    !string.IsNullOrEmpty(k.Locale)
                    && string.Equals(
                        LanguageSubtag(k.Locale),
                        wantLang,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();
        }

        /// <summary>
        /// Best installed OS keyboard for the language: an exact locale match wins, otherwise the first
        /// language-only match. Returns null if this machine has no input method for the language.
        /// UI-thread-only.
        /// </summary>
        public OsKeyboardInfo FindBestForLanguage(string tag)
        {
            var candidates = GetInstalledKeyboardsForLanguage(tag).ToList();
            if (candidates.Count == 0)
                return null;
            var exact = candidates.FirstOrDefault(k =>
                string.Equals(k.Locale, tag, StringComparison.OrdinalIgnoreCase)
            );
            return ToInfo(exact ?? candidates[0]);
        }

        /// <summary>
        /// The installed OS keyboard with the given libpalaso Id, or null if it isn't installed here
        /// (in which case a pinned "system:" setting should fall through to Automatic). UI-thread-only.
        /// </summary>
        public OsKeyboardInfo FindById(string libPalasoKeyboardId)
        {
            if (string.IsNullOrWhiteSpace(libPalasoKeyboardId))
                throw new ArgumentException(
                    "A keyboard id is required.",
                    nameof(libPalasoKeyboardId)
                );
            if (!KeyboardController.IsInitialized)
                return null;
            if (!Keyboard.Controller.TryGetKeyboard(libPalasoKeyboardId, out var kb))
                return null;
            return ToInfo(kb);
        }

        /// <summary>
        /// Switch Bloom's input language to the given keyboard by posting WM_INPUTLANGCHANGEREQUEST to
        /// Bloom's main window (the mechanism proven by the spike). Returns false if no HKL could be
        /// resolved or there is no main window. UI-thread-only.
        /// </summary>
        public bool Activate(OsKeyboardInfo keyboard)
        {
            if (keyboard == null)
                throw new ArgumentNullException(nameof(keyboard));
            if (keyboard.IsTip)
            {
                // Residual risk: for a TSF TIP (e.g. an installed Keyman-for-Windows keyboard) posting
                // the language's HKL selects the language, but which profile within that language wins
                // was NOT verified by the spike. If this proves wrong in the field, a TIP-specific
                // activation strategy (selecting the exact profile) can be added here.
                Debug.WriteLine(
                    $"OsKeyboards: activating TIP-backed keyboard '{keyboard.Id}' via HKL 0x{keyboard.Hkl.ToInt64():X} (TIP profile selection unverified)."
                );
            }
            return PostInputLanguageChange(keyboard.Hkl);
        }

        /// <summary>
        /// Switch Bloom's input language back to the default (US English) layout. Used for fields with
        /// no writing system (source-bubble/xmatter placeholders) and as the Automatic last resort.
        /// UI-thread-only.
        /// </summary>
        public bool ActivateDefault()
        {
            var hkl = LoadKeyboardLayout(kUsEnglishKlid, KLF_NOTELLSHELL);
            return PostInputLanguageChange(hkl);
        }

        /// <summary>
        /// Post WM_INPUTLANGCHANGEREQUEST(hkl) to Bloom's main window. Returns false if the HKL is null
        /// or there is no window to post to.
        /// </summary>
        private static bool PostInputLanguageChange(IntPtr hkl)
        {
            if (hkl == IntPtr.Zero)
                return false;
            var hwnd = GetMainWindowHandle();
            if (hwnd == IntPtr.Zero)
                return false;
            // INPUTLANGCHANGE_SYSCHARSET matches the message a language-bar / Win+Space switch sends;
            // this is the same request the spike used to flip the webview process's input thread.
            PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, INPUTLANGCHANGE_SYSCHARSET, hkl);
            return true;
        }

        /// <summary>
        /// Bloom's main (Shell) top-level window handle, or IntPtr.Zero if none is open. The webview
        /// process follows the input language of this window.
        /// </summary>
        private static IntPtr GetMainWindowHandle()
        {
            var shell =
                Shell.GetShellOrOtherOpenForm()
                ?? Application.OpenForms.Cast<Form>().FirstOrDefault();
            return shell?.Handle ?? IntPtr.Zero;
        }

        /// <summary>
        /// Build an <see cref="OsKeyboardInfo"/> for a libpalaso keyboard, resolving its HKL from the
        /// language subtag. Marks IsTip when we could not find a matching loaded HKL for the language
        /// (a heuristic: TSF TIPs frequently have no plain layout loaded for the langid).
        /// </summary>
        private static OsKeyboardInfo ToInfo(IKeyboardDefinition kb)
        {
            var langid = TryGetLangId(kb.Locale);
            var loadedHkl = langid == 0 ? IntPtr.Zero : FindLoadedHklForLangId(langid);
            var hkl = loadedHkl;
            var isTip = false;
            if (hkl == IntPtr.Zero && langid != 0)
            {
                // No plain layout is loaded for this language; load the language's default layout so we
                // still have an HKL to post. Treat as (probably) TIP-backed for logging purposes.
                hkl = LoadKeyboardLayout(langid.ToString("X4").PadLeft(8, '0'), KLF_NOTELLSHELL);
                isTip = true;
            }
            return new OsKeyboardInfo
            {
                Id = kb.Id,
                Locale = kb.Locale,
                Hkl = hkl,
                IsTip = isTip,
            };
        }

        /// <summary>
        /// The loaded HKL whose language id (low word) matches <paramref name="langid"/>, or
        /// IntPtr.Zero if none is loaded.
        /// </summary>
        private static IntPtr FindLoadedHklForLangId(int langid)
        {
            var count = GetKeyboardLayoutList(0, null);
            if (count <= 0)
                return IntPtr.Zero;
            var list = new IntPtr[count];
            GetKeyboardLayoutList(count, list);
            return list.FirstOrDefault(h => (h.ToInt64() & 0xFFFF) == langid);
        }

        /// <summary>
        /// The Windows language id (low word of the LCID) for a locale like "th" or "th-TH", or 0 if it
        /// can't be determined (unknown/custom locale).
        /// </summary>
        private static int TryGetLangId(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                return 0;
            try
            {
                var lcid = new CultureInfo(locale).LCID;
                if (lcid == 0x1000 || lcid == 0x7F) // LOCALE_CUSTOM_UNSPECIFIED / invariant
                    return 0;
                return lcid & 0xFFFF;
            }
            catch (CultureNotFoundException)
            {
                return 0;
            }
        }

        /// <summary>The language subtag (portion before the first '-'), lowercased.</summary>
        private static string LanguageSubtag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return "";
            var dash = tag.IndexOf('-');
            return (dash < 0 ? tag : tag.Substring(0, dash)).ToLowerInvariant();
        }
    }
}
