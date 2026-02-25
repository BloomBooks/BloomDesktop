using System;
using System.Collections.Concurrent;
using Bloom.Api;

namespace Bloom.web
{
    /// <summary>
    /// Severity values for toast notifications sent over websocket.
    /// Keep these values in sync with ToastSeverity in src/BloomBrowserUI/toast/Toast.tsx.
    /// </summary>
    public static class ToastSeverity
    {
        public const string Error = "error";
        public const string Warning = "warning";
        public const string Notice = "notice";
    }

    /// <summary>
    /// Optional action metadata attached to a toast.
    /// Keep property names and semantics in sync with IToastAction in src/BloomBrowserUI/toast/Toast.tsx.
    /// </summary>
    public class ToastAction
    {
        public string Label { get; set; }
        public string L10nId { get; set; }
        public string Url { get; set; }
        public Action Callback { get; set; }
        public int? CallbackTimeoutSeconds { get; set; }
    }

    /// <summary>
    /// Sends toast show events to the browser UI and dispatches optional action callbacks.
    /// </summary>
    public static class ToastService
    {
        private const string kToastClientContext = "toast";
        private const string kToastShowEvent = "show";

        /// <summary>
        /// Tracks callback actions that can be executed later from browser toast interactions.
        /// </summary>
        private class CallbackRecord
        {
            public Action Action { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CallbackRecord> s_callbackActions =
            new ConcurrentDictionary<string, CallbackRecord>();

        public static string ShowToast(
            string severity = ToastSeverity.Notice,
            string text = null,
            string l10nId = null,
            string l10nDefaultText = null,
            int? durationSeconds = null,
            string dedupeKey = null,
            ToastAction action = null,
            string toastId = null
        )
        {
            CleanupExpiredCallbacks();
            var resolvedToastId = toastId ?? Guid.NewGuid().ToString("N");

            dynamic bundle = new DynamicJson();
            bundle.toastId = resolvedToastId;
            bundle.severity = severity;

            if (!string.IsNullOrWhiteSpace(text))
                bundle.text = text;
            if (!string.IsNullOrWhiteSpace(l10nId))
                bundle.l10nId = l10nId;
            if (!string.IsNullOrWhiteSpace(l10nDefaultText))
                bundle.l10nDefaultText = l10nDefaultText;
            if (durationSeconds.HasValue)
                bundle.durationSeconds = durationSeconds.Value;
            if (string.IsNullOrWhiteSpace(dedupeKey))
                dedupeKey =
                    !string.IsNullOrWhiteSpace(text) ? text
                    : !string.IsNullOrWhiteSpace(l10nId) ? l10nId
                    : null;
            if (!string.IsNullOrWhiteSpace(dedupeKey))
                bundle.dedupeKey = dedupeKey;

            if (action != null)
            {
                dynamic actionBundle = new DynamicJson();
                if (!string.IsNullOrWhiteSpace(action.Label))
                    actionBundle.label = action.Label;
                if (!string.IsNullOrWhiteSpace(action.L10nId))
                    actionBundle.l10nId = action.L10nId;
                if (!string.IsNullOrWhiteSpace(action.Url))
                    actionBundle.url = action.Url;

                if (action.Callback != null)
                {
                    var callbackTimeoutSeconds =
                        action.CallbackTimeoutSeconds
                        ?? (
                            durationSeconds.HasValue
                                ? Math.Max(600, durationSeconds.Value + 120)
                                : 7 * 24 * 60 * 60
                        );
                    var callbackId = Guid.NewGuid().ToString("N");
                    s_callbackActions[callbackId] = new CallbackRecord
                    {
                        Action = action.Callback,
                        ExpiresUtc = DateTime.UtcNow.AddSeconds(callbackTimeoutSeconds),
                    };
                    actionBundle.callbackId = callbackId;
                }

                bundle.action = actionBundle;
            }

            BloomWebSocketServer.Instance?.SendBundle(kToastClientContext, kToastShowEvent, bundle);
            return resolvedToastId;
        }

        public static bool PerformAction(string callbackId)
        {
            CleanupExpiredCallbacks();
            if (string.IsNullOrWhiteSpace(callbackId))
                return false;

            if (!s_callbackActions.TryRemove(callbackId, out var callbackRecord))
                return false;

            Program.MainContext.Post(_ => callbackRecord.Action(), null);
            return true;
        }

        private static void CleanupExpiredCallbacks()
        {
            var now = DateTime.UtcNow;
            foreach (var callbackPair in s_callbackActions)
            {
                if (callbackPair.Value.ExpiresUtc <= now)
                    s_callbackActions.TryRemove(callbackPair.Key, out _);
            }
        }
    }
}
