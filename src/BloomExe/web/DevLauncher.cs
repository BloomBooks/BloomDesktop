using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SIL.Reporting;

namespace Bloom.web
{
    /// <summary>
    /// Developer-only glue between Bloom and the dev launcher (go.sh /
    /// scripts/watchBloomExe.mjs). A launcher-started Bloom gets the launcher's
    /// control port on the command line (--launcher-port); we poll that
    /// launcher's /status and, once it reports that dotnet watch has seen C#
    /// source changes since this Bloom became ready, show a non-expiring toast
    /// whose action asks the launcher to quit Bloom, rebuild, and relaunch it.
    ///
    /// None of this happens for end users: without --launcher-port every method
    /// here is a no-op.
    /// </summary>
    public static class DevLauncher
    {
        // One identity for every show event, so the browser keeps a single
        // visible toast and ToastService reuses a single callback registration
        // instead of accumulating one per poll.
        private const string kRestartToastId = "dev-launcher-restart";

        // Deliberately not localized; developers are the only audience.
        private const string kRestartToastText =
            "C# code has changed since this Bloom started. Restarting will rebuild and relaunch it.";
        private const string kRestartToastActionLabel = "Restart";

        private static readonly TimeSpan kPollInterval = TimeSpan.FromSeconds(5);

        // These are tiny loopback calls to the launcher; one that does not answer
        // promptly is effectively dead, hence the short timeout.
        private static readonly HttpClient s_client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        private static bool s_monitoring;
        private static bool s_restartRequested;

        /// <summary>
        /// Begin watching the dev launcher for pending C# changes, showing the
        /// restart toast whenever there are some. Safe to call more than once
        /// (only the first call does anything), and a no-op unless the dev
        /// launcher started this Bloom.
        /// </summary>
        public static void StartMonitoringForSourceChanges()
        {
            if (s_monitoring || Program.StartupLauncherPort == null)
                return;

            s_monitoring = true;
            // Fire and forget: this loop lives as long as the process does.
            Task.Run(MonitorForSourceChangesAsync);
        }

        /// <summary>
        /// Poll the launcher for the life of the process, offering the restart
        /// toast whenever it reports pending C# changes. While a restart request
        /// is in flight we stay quiet (this process is about to be replaced), but
        /// the loop keeps running so a request the launcher never honors leaves
        /// the developer with the offer again rather than with nothing.
        /// The toast is re-sent on every poll rather than only when the change
        /// flag flips, because the dev loop reloads the browser page often
        /// (Vite) and a reload wipes the toast stack. Re-sending restores it;
        /// while the toast is still on screen the browser treats the repeat as
        /// a duplicate and leaves the existing one alone.
        /// </summary>
        private static async Task MonitorForSourceChangesAsync()
        {
            while (true)
            {
                await Task.Delay(kPollInterval);

                if (!s_restartRequested && await HasLauncherSeenSourceChangesAsync())
                    ShowRestartToast();
            }
        }

        /// <summary>
        /// Ask the launcher whether dotnet watch has seen C# source changes
        /// since this Bloom reported ready — i.e. whether a restart would
        /// actually incorporate .NET changes. False if the launcher is gone or
        /// does not answer.
        /// </summary>
        private static async Task<bool> HasLauncherSeenSourceChangesAsync()
        {
            try
            {
                var body = await s_client.GetStringAsync(
                    $"http://127.0.0.1:{Program.StartupLauncherPort}/status"
                );
                return JObject.Parse(body)["sourceChangedSinceReady"]?.Value<bool>() == true;
            }
            catch (Exception)
            {
                // The launcher has probably exited; nothing to offer.
                return false;
            }
        }

        private static void ShowRestartToast()
        {
            // Update, not Warning: nothing is wrong, there is just a newer build
            // waiting — the same message Bloom's own "new version downloaded"
            // toast carries.
            ToastService.ShowToast(
                ToastType.Update,
                text: kRestartToastText,
                action: new ToastAction
                {
                    Label = kRestartToastActionLabel,
                    Callback = RequestRestart,
                },
                toastId: kRestartToastId
            );
        }

        /// <summary>
        /// Ask the dev launcher that started us to quit Bloom, rebuild, and
        /// relaunch it. The launcher accepts with a 202 and then closes this very
        /// process as part of the restart, so we do not wait for the restart
        /// itself — only for the launcher to accept the request.
        /// </summary>
        private static void RequestRestart()
        {
            Logger.WriteEvent("Dev launcher restart requested from the restart toast.");
            // Stop polling while the request is in flight: the launcher is about to
            // take this process down, and re-showing the toast meanwhile is noise.
            s_restartRequested = true;
            Task.Run(RequestRestartAsync);
        }

        private static async Task RequestRestartAsync()
        {
            try
            {
                var response = await s_client.PostAsync(
                    $"http://127.0.0.1:{Program.StartupLauncherPort}/restart",
                    new StringContent("")
                );
                if (response.IsSuccessStatusCode)
                    return;

                Logger.WriteEvent(
                    $"The dev launcher refused the restart request: {(int)response.StatusCode}."
                );
            }
            catch (Exception error)
            {
                Logger.WriteEvent($"Could not reach the dev launcher to restart: {error.Message}");
            }

            // Nobody is going to take this Bloom down after all (the launcher is
            // gone, or refused), and ToastService already consumed the toast's
            // one-shot callback. Let the still-running poll loop offer the restart
            // again rather than leaving the developer with no affordance.
            s_restartRequested = false;
        }
    }
}
