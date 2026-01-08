using System;
using System.ComponentModel;
using System.IO;

namespace Bloom.Utils
{
    /// <summary>
    /// Wraps a FileSystemWatcher and exposes all its public members, plus an IsDisposed property.
    /// This allows the debounce extension methods to avoid sending a notification after the watcher
    /// has been disposed, which can mess up unit tests.
    /// </summary>
    public class FileSystemWatcherWrapper : IDisposable, ISupportInitialize
    {
        private readonly FileSystemWatcher _watcher;
        private bool _isDisposed;

        public FileSystemWatcherWrapper()
        {
            _watcher = new FileSystemWatcher();
        }

        public FileSystemWatcherWrapper(string path)
        {
            _watcher = new FileSystemWatcher(path);
        }

        public FileSystemWatcherWrapper(string path, string filter)
        {
            _watcher = new FileSystemWatcher(path, filter);
        }

        public bool IsDisposed => _isDisposed;

        public bool EnableRaisingEvents
        {
            get => _watcher.EnableRaisingEvents;
            set => _watcher.EnableRaisingEvents = value;
        }

        public string Filter
        {
            get => _watcher.Filter;
            set => _watcher.Filter = value;
        }

        public string Path
        {
            get => _watcher.Path;
            set => _watcher.Path = value;
        }

        public NotifyFilters NotifyFilter
        {
            get => _watcher.NotifyFilter;
            set => _watcher.NotifyFilter = value;
        }

        public bool IncludeSubdirectories
        {
            get => _watcher.IncludeSubdirectories;
            set => _watcher.IncludeSubdirectories = value;
        }

        public int InternalBufferSize
        {
            get => _watcher.InternalBufferSize;
            set => _watcher.InternalBufferSize = value;
        }

        public event FileSystemEventHandler Changed
        {
            add => _watcher.Changed += value;
            remove => _watcher.Changed -= value;
        }

        public event FileSystemEventHandler Created
        {
            add => _watcher.Created += value;
            remove => _watcher.Created -= value;
        }

        public event FileSystemEventHandler Deleted
        {
            add => _watcher.Deleted += value;
            remove => _watcher.Deleted -= value;
        }

        public event RenamedEventHandler Renamed
        {
            add => _watcher.Renamed += value;
            remove => _watcher.Renamed -= value;
        }

        public event ErrorEventHandler Error
        {
            add => _watcher.Error += value;
            remove => _watcher.Error -= value;
        }

        public void BeginInit() => ((ISupportInitialize)_watcher).BeginInit();

        public void EndInit() => ((ISupportInitialize)_watcher).EndInit();

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _watcher.Dispose();
            }
        }
    }
}
