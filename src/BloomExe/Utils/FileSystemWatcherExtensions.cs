using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Utils
{
    // NOTE: This code derived from https://stackoverflow.com/a/58079327/311238
    // Modifications:
    // * OnAnyEvent -> DebounceAnyEvent. Likewise for OnAllEvents, OnChanged, etc.

    /// <summary>
    /// This class adds extensions to FileSystemWatcherWrapper to debounce FileSystemWatcher events.
    /// The idea is to enhance FileSystemWatcher, but the FileSystemWatcher must be wrapped in a FileSystemWatcherWrapper
    /// so that this class can detect if it has been disposed and not send notifications after that.
    /// The debouncing basically means if multiple events of the specified {changeTypes} happen within the delay ,
    /// {handler} will only be invoked once, and it is for the last one.
    /// </summary>
    public static class FileSystemWatcherExtensions
    {
        public static IDisposable DebounceAnyEvent(
            this FileSystemWatcherWrapper source,
            WatcherChangeTypes changeTypes,
            FileSystemEventHandler handler,
            int delay
        )
        {
            var cancellations = new Dictionary<string, CancellationTokenSource>(
                StringComparer.OrdinalIgnoreCase
            );
            var locker = new object();
            if (changeTypes.HasFlag(WatcherChangeTypes.Created))
                source.Created += FileSystemWatcher_EventAsync;
            if (changeTypes.HasFlag(WatcherChangeTypes.Deleted))
                source.Deleted += FileSystemWatcher_EventAsync;
            if (changeTypes.HasFlag(WatcherChangeTypes.Changed))
                source.Changed += FileSystemWatcher_EventAsync;
            if (changeTypes.HasFlag(WatcherChangeTypes.Renamed))
                source.Renamed += FileSystemWatcher_EventAsync;
            return new Disposable(() =>
            {
                source.Created -= FileSystemWatcher_EventAsync;
                source.Deleted -= FileSystemWatcher_EventAsync;
                source.Changed -= FileSystemWatcher_EventAsync;
                source.Renamed -= FileSystemWatcher_EventAsync;
            });

            async void FileSystemWatcher_EventAsync(object sender, FileSystemEventArgs e)
            {
                var key = e.FullPath;
                var cts = new CancellationTokenSource();
                lock (locker)
                {
                    if (cancellations.TryGetValue(key, out var existing))
                    {
                        existing.Cancel();
                    }
                    cancellations[key] = cts;
                }
                try
                {
                    await Task.Delay(delay, cts.Token);
                    // Omitting ConfigureAwait(false) is intentional here.
                    // Continuing in the captured context is desirable.
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                lock (locker)
                {
                    if (cancellations.TryGetValue(key, out var existing) && existing == cts)
                    {
                        cancellations.Remove(key);
                    }
                }
                cts.Dispose();
                // Don't send notifications if the watcher has been disposed or is not enabled.
                // It's somewhat debatable whether we might want a notification for something that
                // happened before we turned off EnableRaisingEvents, but in practice,
                // unit tests get messed up by notifications arising after we've attempted to
                // shut everything down. I think it's safer this way even for normal operation.
                if (!source.IsDisposed && source.EnableRaisingEvents)
                    handler(sender, e);
            }
        }

        public static IDisposable DebounceAllEvents(
            this FileSystemWatcherWrapper source,
            FileSystemEventHandler handler,
            int delay
        ) => DebounceAnyEvent(source, WatcherChangeTypes.All, handler, delay);

        public static IDisposable DebounceCreated(
            this FileSystemWatcherWrapper source,
            FileSystemEventHandler handler,
            int delay
        ) => DebounceAnyEvent(source, WatcherChangeTypes.Created, handler, delay);

        public static IDisposable DebounceDeleted(
            this FileSystemWatcherWrapper source,
            FileSystemEventHandler handler,
            int delay
        ) => DebounceAnyEvent(source, WatcherChangeTypes.Deleted, handler, delay);

        public static IDisposable DebounceChanged(
            this FileSystemWatcherWrapper source,
            FileSystemEventHandler handler,
            int delay
        ) => DebounceAnyEvent(source, WatcherChangeTypes.Changed, handler, delay);

        public static IDisposable DebounceRenamed(
            this FileSystemWatcherWrapper source,
            FileSystemEventHandler handler,
            int delay
        ) => DebounceAnyEvent(source, WatcherChangeTypes.Renamed, handler, delay);

        private struct Disposable : IDisposable
        {
            private readonly Action _action;

            internal Disposable(Action action) => _action = action;

            public void Dispose() => _action?.Invoke();
        }
    }
}
