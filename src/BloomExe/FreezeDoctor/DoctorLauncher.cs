using System;
using System.Diagnostics;
using System.IO;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.FreezeDoctor
{
    /// <summary>
    /// Starts the Bloom Freeze Doctor, if the user has installed it.
    ///
    /// **The Doctor is only useful if it is already running when Bloom stops responding**, which is why
    /// Bloom starts it rather than leaving it to the user: nobody launches a diagnostic tool *before* the
    /// thing they are diagnosing goes wrong. It is installed separately, so this is opt-in by construction —
    /// on a machine without the Doctor, all of this costs one directory check.
    ///
    /// **There is deliberately no handshake.** Bloom's only job is to make sure a Doctor is running; the
    /// Doctor decides for itself which Blooms to watch, by holding a single-instance mutex and scanning for
    /// them. So starting a second Doctor is harmless — it hands off to the one already running and exits —
    /// and Bloom never has to know or care which case it is in. Nothing here waits for the Doctor, checks
    /// on it, or is affected by whether it worked.
    /// </summary>
    public static class DoctorLauncher
    {
        /// <summary>The installed executable's name.</summary>
        private const string ExecutableName = "BloomFreezeDoctor.exe";

        /// <summary>
        /// Set this to skip launching, for a developer who wants Bloom without a Doctor attaching itself.
        /// </summary>
        public const string SuppressEnvironmentVariable = "BLOOM_NO_FREEZE_DOCTOR";

        /// <summary>
        /// Starts the Doctor if it is installed, telling it which process to watch. Returns immediately and
        /// never throws: this is a diagnostic convenience, and it must not be able to affect Bloom's startup
        /// in any way a user would notice.
        /// </summary>
        public static void LaunchIfInstalled()
        {
            try
            {
                if (
                    !string.IsNullOrEmpty(
                        Environment.GetEnvironmentVariable(SuppressEnvironmentVariable)
                    )
                )
                    return;

                var exe = FindInstalledDoctor();
                if (exe == null)
                    return; // Not installed. This is the normal case, and costs one directory check.

                Process.Start(
                    new ProcessStartInfo(exe)
                    {
                        // --adopt tells it which Bloom we are, and also means "start out of the way in the
                        // notification area": a window appearing every time you started Bloom would get the
                        // Doctor uninstalled inside a week.
                        Arguments = "--adopt " + Process.GetCurrentProcess().Id,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        // Do not inherit our working directory: Bloom's can be a collection folder, and
                        // holding it open would stop the user moving or renaming it.
                        WorkingDirectory = Path.GetDirectoryName(exe),
                    }
                );
                Logger.WriteEvent("Asked the Bloom Freeze Doctor to watch this process");
            }
            catch (Exception e)
            {
                // Swallowed on purpose. If the Doctor cannot be started, Bloom carries on exactly as it
                // would on a machine where it was never installed.
                try
                {
                    Logger.WriteEvent(
                        "Could not start the Bloom Freeze Doctor (continuing without it): "
                            + e.Message
                    );
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Looks for an installed Doctor. Velopack installs per-user into
        /// <c>%LOCALAPPDATA%\BloomFreezeDoctor\current\</c>, plus an environment variable so a developer
        /// can point at a build tree.
        ///
        /// Only that one installed shape is looked for, deliberately. An earlier version also checked the
        /// parent directory, in case a future Velopack went back to putting the launcher there — but that
        /// is speculation about a direction Velopack seems unlikely to reverse, and the cost of being wrong
        /// is small and obvious: somebody on a newer Doctor launches it by hand. A directory check that
        /// exists for a hypothetical is a directory check nobody can ever prove is still needed.
        /// </summary>
        private static string FindInstalledDoctor()
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            var candidates = new[]
            {
                // The installed layout.
                Path.Combine(localAppData, "BloomFreezeDoctor", "current", ExecutableName),
                // An explicit override, for testing against a build tree.
                Environment.GetEnvironmentVariable("BLOOM_FREEZE_DOCTOR_PATH"),
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(candidate) && RobustFile.Exists(candidate))
                        return candidate;
                }
                catch (Exception)
                {
                    // A malformed path in the environment variable, most likely; try the next.
                }
            }
            return null;
        }
    }
}
