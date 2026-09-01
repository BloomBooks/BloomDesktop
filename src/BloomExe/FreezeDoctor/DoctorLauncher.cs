using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Bloom.Properties;
using BloomFreezeDoctor.Protocol;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.FreezeDoctor
{
    /// <summary>
    /// Starts the Bloom Freeze Doctor, if the user has switched it on.
    ///
    /// **The Doctor is only useful if it is already running when Bloom stops responding**, which is why
    /// Bloom starts it rather than leaving it to the user: nobody launches a diagnostic tool *before* the
    /// thing they are diagnosing goes wrong. It ships inside Bloom's installer, so the
    /// <see cref="Settings.RunFreezeDoctor"/> setting is what keeps everyone from paying for a watcher they
    /// did not ask for; with it off, all of this costs one setting read.
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
        /// Tells any Doctor that is ALREADY RUNNING that a Bloom has started, so it adopts us now instead
        /// of at its next five-second sweep.
        ///
        /// Needed as well as starting one below, because the two cover different cases: starting one covers
        /// "no Doctor yet", and a Doctor Bloom starts when one is already running is a duplicate that exits
        /// on the singleton mutex without ever telling the original that we exist. That is why adoption used
        /// to wait for a poll.
        ///
        /// **Called from the very top of Program.Main, and that placement is the whole value.** Everything
        /// before it is time in which a hang or a crash cannot be doctored at all, since Bloom only asks for
        /// a dump when a Doctor is already watching. Announced from further down - where the Doctor is
        /// launched - it was measured arriving 6.2 seconds into startup, by which time the sweep had already
        /// found us and it had bought nothing. It can go first because it needs nothing: one named event,
        /// set, and return.
        ///
        /// Gated on <see cref="Settings.RunFreezeDoctor"/>, so it costs nothing at all for the users who
        /// have never switched the Doctor on. It briefly was not, on the argument that support may have had
        /// a user start a Doctor by hand with the setting off - which does not survive examination, as John
        /// pointed out. Both support routes are covered without it: told to switch it on and relaunch, the
        /// setting is on; told to start a Doctor while Bloom is already running, this has long since run and
        /// the Doctor's own sweep is what finds Bloom. What ungating actually bought was at most one poll
        /// interval in the narrow case of a Doctor left running while the setting is off, and that is not
        /// worth a line in every user's startup.
        /// </summary>
        public static void AnnounceToAnyDoctor()
        {
            try
            {
                if (!Settings.Default.RunFreezeDoctor)
                    return;
                DoctorSignals.Announce(DoctorSignals.BloomStartedName());
            }
            catch (Exception)
            {
                // Swallowed like everything else here. A diagnostic convenience must never be able to
                // affect Bloom's startup.
            }
        }

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
                // Again, and not redundantly: this is also the debug-menu path, where a Doctor may have been
                // started since Main announced. Setting an auto-reset event nobody is waiting on costs
                // nothing and is thrown away by the next waiter.
                AnnounceToAnyDoctor();

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
        /// BloomFreezeDoctor.exe, or null if this build did not produce one. It is always beside Bloom.exe -
        /// built into Bloom's own output directory and shipped by Bloom's installer, exactly like
        /// BloomPdfMaker.exe - so there is nowhere to search.
        /// </summary>
        private static string FindTheDoctor()
        {
            var beside = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath) ?? "",
                ExecutableName
            );
            return RobustFile.Exists(beside) ? beside : null;
        }
    }
}
