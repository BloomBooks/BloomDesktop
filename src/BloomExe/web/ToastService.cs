using System;
using System.Collections.Concurrent;
using Bloom.Api;

namespace Bloom.web
{
    /// <summary>
    /// Type values for toast notifications sent over websocket.
    /// Keep in sync with ToastType in src/BloomBrowserUI/toast/Toast.tsx.
    /// </summary>
    public static class ToastType
    {
        public const string Error = "error";
        public const string Warning = "warning";
        public const string Notice = "notice";
        public const string Update = "update";
    }

    /// <summary>
    /// Optional action metadata attached to a toast.
    /// Keep in sync with ToastActionInfo in src/BloomBrowserUI/toast/Toast.tsx.
    /// </summary>
    public class ToastAction
    {
        public string Label { get; set; }
        public string L10nId { get; set; }
        public Action Callback { get; set; }
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

        /// <summary>
        /// Show a toast in the browser UI.
        /// Use toastId when repeated calls are really the same logical toast and should share one
        /// visible instance and one callback registration. This is especially important for
        /// persistent toasts with actions, where the browser may dedupe repeated show events while
        /// the backend would otherwise keep registering new callbacks.
        /// Do not use toastId for unrelated toasts that merely happen to have similar text; those
        /// should remain independent so each one can carry its own lifecycle and action.
        /// </summary>
        public static void ShowToast(
            string type = ToastType.Notice,
            string text = null,
            string l10nId = null,
            int? durationSeconds = null,
            ToastAction action = null,
            string toastId = null
        )
        {
            CleanupExpiredCallbacks();

            dynamic bundle = new DynamicJson();
            bundle.type = type;

            if (!string.IsNullOrWhiteSpace(text))
                bundle.text = text;
            if (!string.IsNullOrWhiteSpace(l10nId))
                bundle.l10nId = l10nId;
            if (durationSeconds.HasValue)
                bundle.durationSeconds = durationSeconds.Value;

            if (action != null)
            {
                var label = string.IsNullOrWhiteSpace(action.Label) ? null : action.Label;
                var actionL10nId = string.IsNullOrWhiteSpace(action.L10nId) ? null : action.L10nId;
                string callbackId = null;

                if (action.Callback != null)
                {
                    var expiresUtc = durationSeconds.HasValue
                        ? DateTime.UtcNow.AddSeconds(Math.Max(600, durationSeconds.Value + 120))
                        : DateTime.MaxValue;
                    // A toastId makes repeated instances of the same logical toast reuse one
                    // callback slot instead of accumulating callbacks the UI will never expose.
                    callbackId = string.IsNullOrWhiteSpace(toastId)
                        ? Guid.NewGuid().ToString("N")
                        : $"toast:{toastId}";
                    s_callbackActions[callbackId] = new CallbackRecord
                    {
                        Action = action.Callback,
                        ExpiresUtc = expiresUtc,
                    };
                }

                if (
                    !string.IsNullOrWhiteSpace(label)
                    || !string.IsNullOrWhiteSpace(actionL10nId)
                    || !string.IsNullOrWhiteSpace(callbackId)
                )
                {
                    bundle.actionInfo = new
                    {
                        label,
                        l10nId = actionL10nId,
                        callbackId,
                    };
                }
            }

            BloomWebSocketServer.Instance?.SendBundle(kToastClientContext, kToastShowEvent, bundle);
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
