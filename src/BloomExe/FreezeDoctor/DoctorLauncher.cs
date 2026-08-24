using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Bloom.Properties;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.FreezeDoctor
{
    /// <summary>
    /// Starts the Bloom Freeze Doctor, if the user has switched it on.
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
        /// Starts the Doctor if the user has asked for it, telling it which process to watch. Returns
        /// immediately and never throws: this is a diagnostic convenience, and it must not be able to
        /// affect Bloom's startup in any way a user would notice.
        ///
        /// Called at startup, and again when somebody switches it on from the debug menu, so that turning
        /// it on takes effect straight away rather than at the next restart. Starting a second one is
        /// harmless: the Doctor holds a singleton mutex and a duplicate exits immediately.
        /// </summary>
        public static void LaunchIfWanted()
        {
            try
            {
                if (
                    !string.IsNullOrEmpty(
                        Environment.GetEnvironmentVariable(SuppressEnvironmentVariable)
                    )
                )
                    return;

                // Off unless asked for. The Doctor now ships inside Bloom rather than being installed
                // separately, so this setting is what stops everyone paying for a watcher they did not
                // ask for - it is the switch that "just don't install it" used to be.
                if (!Settings.Default.RunFreezeDoctor)
                    return;

                var exe = FindTheDoctor();
                if (exe == null)
                    return; // A build that did not produce it. Nothing to do, and nothing to say.

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
        /// Finds BloomFreezeDoctor.exe, which lives beside Bloom.exe: it is built into Bloom's own output
        /// directory and shipped by Bloom's installer, exactly like BloomPdfMaker.exe.
        ///
        /// It used to be a separately installed application, and this method used to go looking for it
        /// under <c>%LOCALAPPDATA%</c> where Velopack would have put it. Sharing Bloom's installer means
        /// there is nowhere to search: either the file is next to us or this is a build that did not
        /// produce it. The environment variable remains, for pointing at a build tree.
        /// </summary>
        private static string FindTheDoctor()
        {
            var candidates = new[]
            {
                // An explicit override first, so a developer testing a build tree wins over whatever
                // happens to be installed beside Bloom.
                Environment.GetEnvironmentVariable("BLOOM_FREEZE_DOCTOR_PATH"),
                Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath) ?? "",
                    ExecutableName
                ),
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
