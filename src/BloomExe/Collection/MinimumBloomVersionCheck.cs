using System;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.web;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Collection
{
    /// <summary>
    /// A collection can declare, via the MinimumBloomVersion element of its .bloomCollection file,
    /// the oldest version of Bloom that is allowed to open it. This is in preparation for Cloud
    /// syncing: once a collection has been touched by a version of Bloom that knows how to sync it,
    /// letting an older Bloom loose on it could do real damage.
    ///
    /// At this point there is no UI for setting the flag; it has to be added to the file by hand.
    /// See BL-16690.
    /// </summary>
    public static class MinimumBloomVersionCheck
    {
        /// <summary>
        /// Decide whether this Bloom is allowed to open the given collection.
        /// </summary>
        /// <param name="settingsFilePath">Path to the .bloomCollection file</param>
        /// <param name="minimumVersion">What the collection asked for, as major.minor, when we say no</param>
        /// <returns>true if the collection demands a newer Bloom than we are</returns>
        public static bool IsThisBloomTooOld(string settingsFilePath, out string minimumVersion)
        {
            var declaredVersion = ReadMinimumBloomVersion(settingsFilePath);
            if (IsVersionSufficient(declaredVersion, RunningBloomVersion))
            {
                minimumVersion = "";
                return false;
            }
            // We can only get here if it parsed, since anything we can't parse counts as satisfied.
            minimumVersion = ToMajorMinor(Version.Parse(declaredVersion.Trim()));
            return true;
        }

        /// <summary>
        /// Compare a collection's declared minimum version against the version we are running.
        /// We compare only major and minor, like the other version gates in Bloom
        /// (BookStorage's feature requirements and BookDownload's minVersion), so a minimum of
        /// "6.5" is satisfied by any 6.5.x or later, and a build number in the minimum is ignored.
        ///
        /// The channel (Alpha/Beta/Release) deliberately plays no part: 6.5 means the same thing on
        /// every channel, so an alpha tester who is running ahead of the release is correctly let in.
        /// </summary>
        /// <remarks>Separated out from the file reading so it can be unit tested.</remarks>
        internal static bool IsVersionSufficient(string minimumVersion, Version runningVersion)
        {
            if (string.IsNullOrWhiteSpace(minimumVersion))
                return true; // the normal case: the collection doesn't care

            // Version.TryParse needs at least "major.minor", so a bare "6" lands here too.
            if (!Version.TryParse(minimumVersion.Trim(), out var requiredVersion))
            {
                // Someone hand-edited the file and mistyped. Locking them out of their own
                // collection over a typo would be worse than ignoring it, so just complain to the log.
                Logger.WriteEvent(
                    $"Ignoring unparseable {CollectionSettings.kMinimumBloomVersionElementName} '{minimumVersion}' in collection settings."
                );
                return true;
            }

            if (runningVersion.Major != requiredVersion.Major)
                return runningVersion.Major > requiredVersion.Major;
            return runningVersion.Minor >= requiredVersion.Minor;
        }

        /// <summary>
        /// Read just the minimum version out of the settings file. We can't use CollectionSettings
        /// for this, because we have to answer the question before we commit to building a
        /// ProjectContext around the collection.
        /// </summary>
        internal static string ReadMinimumBloomVersion(string settingsFilePath)
        {
            try
            {
                if (!RobustFile.Exists(settingsFilePath))
                    return "";
                var settingsContent = RobustFile.ReadAllText(settingsFilePath, Encoding.UTF8);
                var xml = XElement.Parse(settingsContent);
                return CollectionSettings.ReadString(
                    xml,
                    CollectionSettings.kMinimumBloomVersionElementName,
                    ""
                );
            }
            catch (Exception ex)
            {
                // A settings file we can't even parse is a real problem, but this is not the place to
                // report it. Let the normal open fail and give the user its much better error report.
                Logger.WriteEvent(
                    $"Could not read {CollectionSettings.kMinimumBloomVersionElementName} from {settingsFilePath}: {ex.Message}"
                );
                return "";
            }
        }

        /// <summary>
        /// Tell the user that this collection needs a newer Bloom, and give them the two ways forward:
        /// upgrade, or open some other collection. If they choose to upgrade we send them to the
        /// downloads page and quit, since they can't install over a running Bloom anyway.
        /// </summary>
        /// <returns>true if the user chose to upgrade, in which case we have already asked Bloom to
        /// quit and the caller must not start any more UI.</returns>
        public static bool ReportCollectionNeedsNewerBloom(
            string collectionName,
            string minimumVersion
        )
        {
            var header = LocalizationManager.GetString(
                "Collection.NewerVersionNeededHeader",
                "This collection needs a newer version of Bloom."
            );
            var explanation = string.Format(
                LocalizationManager.GetString(
                    "Collection.CollectionRequiresNewerVersion",
                    "The collection \"{0}\" requires Bloom {1} or greater. You are running Bloom {2}.",
                    "{0} is the name of the collection, {1} is the version it requires, {2} is the version of Bloom that is running."
                ),
                // The message is HTML, and a collection name can perfectly well contain an ampersand.
                System.Net.WebUtility.HtmlEncode(collectionName),
                minimumVersion,
                ToMajorMinor(RunningBloomVersion)
            );
            var upgradeButtonText = LocalizationManager.GetString(
                "Collection.UpgradeBloom",
                "Upgrade Bloom"
            );
            var chooseOtherButtonText = LocalizationManager.GetString(
                "Collection.OpenDifferentCollection",
                "Open a Different Collection"
            );

            var buttons = new[]
            {
                new MessageBoxButton()
                {
                    Text = upgradeButtonText,
                    Id = kUpgradeButtonId,
                    // Warn the user that this takes them out to a web page.
                    Icon = "launch",
                },
                new MessageBoxButton()
                {
                    Text = chooseOtherButtonText,
                    Id = "chooseOther",
                    Default = true,
                },
            };
            var result = BloomMessageBox.Show(
                null,
                $"<strong>{header}</strong><br/><br/>{explanation}",
                buttons,
                MessageBoxIcon.Warning
            );

            if (result == kUpgradeButtonId)
            {
                ShowDownloadsPage();
                ProgramExit.Exit();
                return true;
            }
            // Otherwise the caller will put the collection chooser back up.
            return false;
        }

        private const string kUpgradeButtonId = "upgrade";

        /// <summary>
        /// Send the user to the page where they can get a newer Bloom. This is the same thing the
        /// app/showDownloadsPage API does for the equivalent message about a book, but we can't use
        /// that API here because we have no book preview browser to put the link in.
        /// </summary>
        private static void ShowDownloadsPage()
        {
            var url = UrlLookup.LookupUrl(UrlType.LibrarySite, null) + "/installers";
            if (SIL.PlatformUtilities.Platform.IsWindows)
                // Let the default browser open the link.
                ProcessExtra.SafeStartInFront(url);
            else
                ProcessExtra.SafeStartInFront("xdg-open", Uri.EscapeUriString(url));
        }

        /// <summary>
        /// Format a version the way we actually compare it: major and minor only. Showing the user a
        /// build number we don't look at would misrepresent the rule, and invites them to compare
        /// the wrong digits ("6.5 required, but I have 6.4.900, and 900 is more than 5").
        /// </summary>
        private static string ToMajorMinor(Version version) => $"{version.Major}.{version.Minor}";

        /// <summary>
        /// The version of Bloom we are running. We use the assembly version rather than
        /// Application.ProductVersion because the latter can carry a non-numeric suffix.
        /// A loaded assembly always has a version, so we don't guard against it being missing:
        /// any fallback we picked would be a lie, and a low one would lock the user out of every
        /// collection that declares a minimum -- the opposite of what this class is careful to do.
        /// </summary>
        private static Version RunningBloomVersion =>
            typeof(MinimumBloomVersionCheck).Assembly.GetName().Version;
    }
}
