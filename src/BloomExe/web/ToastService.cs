using System;
using System.Collections.Concurrent;
using Bloom.Api;

namespace Bloom.web
{
    public static class ToastSeverity
    {
        public const string Error = "error";
        public const string Warning = "warning";
        public const string Notice = "notice";
    }

    public static class ToastActionKind
    {
        public const string Restart = "restart";
        public const string Navigate = "navigate";
        public const string OpenErrorDialog = "openErrorDialog";
        public const string Callback = "callback";
    }

    public class ToastAction
    {
        public string Label { get; set; }
        public string L10nId { get; set; }
        public string Kind { get; set; }
        public string Url { get; set; }
        public Action Callback { get; set; }
        public int CallbackTimeoutSeconds { get; set; } = 600;
    }

    public static class ToastService
    {
        private const string kToastClientContext = "toast";
        private const string kToastShowEvent = "show";
        private const string kToastDismissEvent = "dismiss";

        private class CallbackRecord
        {
            public Action Action { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CallbackRecord> s_callbackActions =
            new ConcurrentDictionary<string, CallbackRecord>();

        public static string ShowToast(
            string severity,
            string text = null,
            string l10nId = null,
            string l10nDefaultText = null,
            bool autoDismiss = true,
            int? durationMs = null,
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
            bundle.autoDismiss = autoDismiss;

            if (!string.IsNullOrWhiteSpace(text))
                bundle.text = text;
            if (!string.IsNullOrWhiteSpace(l10nId))
                bundle.l10nId = l10nId;
            if (!string.IsNullOrWhiteSpace(l10nDefaultText))
                bundle.l10nDefaultText = l10nDefaultText;
            if (durationMs.HasValue)
                bundle.durationMs = durationMs.Value;
            if (!string.IsNullOrWhiteSpace(dedupeKey))
                bundle.dedupeKey = dedupeKey;

            if (action != null)
            {
                dynamic actionBundle = new DynamicJson();
                if (!string.IsNullOrWhiteSpace(action.Label))
                    actionBundle.label = action.Label;
                if (!string.IsNullOrWhiteSpace(action.L10nId))
                    actionBundle.l10nId = action.L10nId;
                if (!string.IsNullOrWhiteSpace(action.Kind))
                    actionBundle.kind = action.Kind;
                if (!string.IsNullOrWhiteSpace(action.Url))
                    actionBundle.url = action.Url;

                if (action.Callback != null)
                {
                    var callbackId = Guid.NewGuid().ToString("N");
                    s_callbackActions[callbackId] = new CallbackRecord
                    {
                        Action = action.Callback,
                        ExpiresUtc = DateTime.UtcNow.AddSeconds(action.CallbackTimeoutSeconds),
                    };
                    actionBundle.callbackId = callbackId;
                }

                bundle.action = actionBundle;
            }

            BloomWebSocketServer.Instance?.SendBundle(kToastClientContext, kToastShowEvent, bundle);
            return resolvedToastId;
        }

        public static void DismissToast(string toastId)
        {
            if (string.IsNullOrWhiteSpace(toastId))
                return;

            dynamic bundle = new DynamicJson();
            bundle.toastId = toastId;
            BloomWebSocketServer.Instance?.SendBundle(
                kToastClientContext,
                kToastDismissEvent,
                bundle
            );
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
