using System;

namespace Bloom.Keyboarding
{
    /// <summary>
    /// Represents the value of a <see cref="Bloom.Collection.WritingSystem.Keyboard"/> setting:
    /// a resolution policy for which keyboard to use when editing a field in that language, not a
    /// literal keyboard reference. Serialized as a single string in the .bloomCollection file.
    /// </summary>
    /// <remarks>
    /// String forms:
    ///   ""                         -> Automatic
    ///   "off"                      -> Off (Bloom leaves the keyboard alone for this language)
    ///   "system:&lt;libpalaso keyboard Id&gt;" -> System (a specific installed OS/TSF input method)
    ///   "kmw:&lt;keyboardId&gt;@&lt;bcp47&gt;"   -> KeymanWeb (a specific pinned Keyman-cloud keyboard)
    /// Parsing is tolerant of malformed/unrecognized input: it degrades to Automatic rather than
    /// throwing, since this is user/file data, not a programmer error.
    /// </remarks>
    public class KeyboardSetting
    {
        /// <summary>
        /// Which kind of keyboard setting this is.
        /// </summary>
        public enum Kind
        {
            /// <summary>Resolve per-machine at edit time: OS keyboard if installed, else the cached KeymanWeb fallback.</summary>
            Automatic,

            /// <summary>Bloom does not manage the keyboard for this language: it neither switches the OS input method nor attaches KeymanWeb, leaving whatever the user has active.</summary>
            Off,

            /// <summary>A specific installed OS/TSF input method, identified by its libpalaso keyboard Id.</summary>
            System,

            /// <summary>A specific pinned KeymanWeb keyboard, identified by its Keyman keyboard id and the BCP-47 tag it was chosen for.</summary>
            KeymanWeb,
        }

        private const string SystemPrefix = "system:";
        private const string KeymanWebPrefix = "kmw:";
        private const string OffValue = "off";

        /// <summary>
        /// The singleton Automatic setting (empty string form).
        /// </summary>
        public static readonly KeyboardSetting Automatic = new KeyboardSetting(
            Kind.Automatic,
            "",
            ""
        );

        /// <summary>
        /// The singleton Off setting.
        /// </summary>
        public static readonly KeyboardSetting Off = new KeyboardSetting(Kind.Off, "", "");

        /// <summary>
        /// Which kind of setting this instance represents.
        /// </summary>
        public Kind SettingKind { get; }

        /// <summary>
        /// For System, the libpalaso keyboard Id. For KeymanWeb, the Keyman keyboard id. Empty for Automatic.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// For KeymanWeb only, the BCP-47 language tag the keyboard was chosen for. Empty otherwise.
        /// </summary>
        public string LanguageTag { get; }

        private KeyboardSetting(Kind kind, string id, string languageTag)
        {
            SettingKind = kind;
            Id = id;
            LanguageTag = languageTag;
        }

        /// <summary>
        /// Creates a setting pinning a specific installed OS/TSF input method.
        /// </summary>
        public static KeyboardSetting CreateSystem(string libPalasoKeyboardId)
        {
            return new KeyboardSetting(Kind.System, libPalasoKeyboardId, "");
        }

        /// <summary>
        /// Creates a setting pinning a specific KeymanWeb keyboard for a given language.
        /// </summary>
        public static KeyboardSetting CreateKeymanWeb(string keyboardId, string bcp47Tag)
        {
            return new KeyboardSetting(Kind.KeymanWeb, keyboardId, bcp47Tag);
        }

        /// <summary>
        /// Parses the serialized form of a keyboard setting. Falls back to <see cref="Automatic"/>
        /// for null/empty/unrecognized/malformed input; never throws.
        /// </summary>
        public static KeyboardSetting Parse(string value)
        {
            if (string.IsNullOrEmpty(value))
                return Automatic;

            if (string.Equals(value, OffValue, StringComparison.Ordinal))
                return Off;

            if (value.StartsWith(SystemPrefix, StringComparison.Ordinal))
            {
                var id = value.Substring(SystemPrefix.Length);
                if (string.IsNullOrEmpty(id))
                    return Automatic; // malformed: no id after the prefix
                return CreateSystem(id);
            }

            if (value.StartsWith(KeymanWebPrefix, StringComparison.Ordinal))
            {
                var rest = value.Substring(KeymanWebPrefix.Length);
                var atIndex = rest.IndexOf('@');
                // Require a non-empty id and a non-empty tag on either side of '@'.
                if (atIndex <= 0 || atIndex == rest.Length - 1)
                    return Automatic; // malformed: missing id, missing '@', or missing tag
                var keyboardId = rest.Substring(0, atIndex);
                var bcp47Tag = rest.Substring(atIndex + 1);
                return CreateKeymanWeb(keyboardId, bcp47Tag);
            }

            return Automatic; // unrecognized form
        }

        /// <summary>
        /// Produces the serialized form of this setting, as understood by <see cref="Parse"/>.
        /// </summary>
        public override string ToString()
        {
            switch (SettingKind)
            {
                case Kind.Off:
                    return OffValue;
                case Kind.System:
                    return SystemPrefix + Id;
                case Kind.KeymanWeb:
                    return KeymanWebPrefix + Id + "@" + LanguageTag;
                default:
                    return "";
            }
        }
    }
}
