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
    /// with the rest of the run. Program.Main skips the "bring in settings from a previous version"
    /// upgrade when a folder is named, for the same reason: the folder holds exactly the settings
    /// its owner put there.
    /// </summary>
    public class BloomSettingsProvider : CrossPlatformSettingsProvider
    {
        /// <summary>
        /// The folder to keep user.config in instead of the usual per-version one, or null for the
        /// usual one. Set once, from the command line, before anything reads Settings.Default:
        /// a provider computes its location when it is constructed, and Settings.Default constructs
        /// its providers the first time any setting is read.
        /// </summary>
        public static string UserSettingsFolder { get; set; }

        public BloomSettingsProvider()
        {
            if (UserSettingsFolder != null)
            {
                UserLocalLocation = UserSettingsFolder;
                UserRoamingLocation = UserSettingsFolder;
            }
        }

        /// <summary>
        /// The folder this process keeps user.config in: UserSettingsFolder when one was named,
        /// otherwise the usual %LOCALAPPDATA%\SIL\Bloom\&lt;version&gt;. Reported through
        /// common/instanceInfo so an automated run can check that the Bloom it launched really is
        /// keeping its settings where it was told to.
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
    }
}
