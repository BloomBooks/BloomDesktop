using System.Configuration;
using System.IO;
using SIL.Settings;

namespace Bloom
{
    /// <summary>
    /// The provider behind Bloom's user settings (Settings.Default, the contents of user.config).
    /// It is libpalaso's CrossPlatformSettingsProvider, which keeps one user.config per build
    /// version in %LOCALAPPDATA%\SIL\Bloom\&lt;version&gt;\, with one addition: a folder named on the
    /// command line (--user-settings-folder) replaces that location, for this process only.
    ///
    /// Every Bloom of one build otherwise shares one user.config, so a Bloom that an automated
    /// test launches would start from whatever the last Bloom of that version saved (the UI
    /// language, the page zoom, the Bloom Library login) and save its own changes for the next one,
    /// including the developer's own Bloom from a worktree of the same version. The e2e launch
    /// fixture therefore gives each Bloom it starts a folder inside the run's temp folder, so its
    /// settings start from defaults, or from whatever the test put there first, and are deleted
    /// with the rest of the run. Such a folder holds exactly the settings its owner put there, so
    /// this provider also brings nothing in from a previous version's user.config when one is
    /// named (see Upgrade below); an automated run would otherwise inherit the developer's
    /// settings after all.
    ///
    /// Only this class knows whether a folder was named. Everything else asks it for what it
    /// needs: where the settings are (GetUserSettingsFolder), or what to put on the command line
    /// of another Bloom so that it uses the same ones (CommandLineArgumentsForChildBloom).
    /// </summary>
    public class BloomSettingsProvider : CrossPlatformSettingsProvider, IApplicationSettingsProvider
    {
        // The folder named on the command line, or null for the usual per-version one.
        private static string _folderFromCommandLine;

        /// <summary>
        /// Keep user.config in this folder instead of the usual per-version one; null restores the
        /// usual one. Program's startup argument parser calls this for --user-settings-folder,
        /// before anything reads Settings.Default: a provider computes its location when it is
        /// constructed, and Settings.Default constructs its providers the first time any setting
        /// is read.
        /// </summary>
        public static void SetUserSettingsFolder(string folder)
        {
            _folderFromCommandLine = folder;
        }

        public BloomSettingsProvider()
        {
            if (_folderFromCommandLine != null)
            {
                UserLocalLocation = _folderFromCommandLine;
                UserRoamingLocation = _folderFromCommandLine;
            }
        }

        /// <summary>
        /// The folder this process keeps user.config in: the one named on the command line when
        /// there was one, otherwise the usual %LOCALAPPDATA%\SIL\Bloom\&lt;version&gt;. Reported
        /// through common/instanceInfo so an automated run can check that the Bloom it launched
        /// really is keeping its settings where it was told to.
        /// </summary>
        public static string GetUserSettingsFolder()
        {
            return new BloomSettingsProvider().UserConfigLocation;
        }

        /// <summary>
        /// The path of the user.config file this process reads and writes.
        /// </summary>
        public static string GetUserConfigPath()
        {
            return Path.Combine(GetUserSettingsFolder(), UserConfigFileName);
        }

        /// <summary>
        /// What to put on the command line of another Bloom this one starts so that it reads and
        /// writes the same user settings as this one: the --user-settings-folder argument when a
        /// folder was named, otherwise nothing, since another Bloom of this build shares the usual
        /// per-version folder anyway.
        /// </summary>
        public static string CommandLineArgumentsForChildBloom =>
            _folderFromCommandLine == null
                ? ""
                : $"--user-settings-folder \"{_folderFromCommandLine}\"";

        // ApplicationSettingsBase drives its providers through IApplicationSettingsProvider, so
        // re-implementing the interface here (libpalaso's methods are not virtual) lets this class
        // decide what "a previous version's settings" means for it: nothing, when a folder was
        // named. Settings.Default.Upgrade() then does exactly what the caller intends in both
        // cases, and no caller needs to know which case it is in.

        /// <summary>
        /// Bring in the settings of a previous Bloom version, unless a folder was named on the
        /// command line, which holds exactly the settings its owner put there.
        /// </summary>
        void IApplicationSettingsProvider.Upgrade(
            SettingsContext context,
            SettingsPropertyCollection properties
        )
        {
            if (_folderFromCommandLine != null)
                return;
            base.Upgrade(context, properties);
        }

        /// <summary>
        /// A setting's value from a previous Bloom version, or null when a folder was named on the
        /// command line: such a folder has no previous version.
        /// </summary>
        SettingsPropertyValue IApplicationSettingsProvider.GetPreviousVersion(
            SettingsContext context,
            SettingsProperty property
        )
        {
            if (_folderFromCommandLine != null)
                return null;
            return base.GetPreviousVersion(context, property);
        }
    }
}
