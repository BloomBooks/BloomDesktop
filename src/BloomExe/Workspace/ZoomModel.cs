using System;

namespace Bloom.Workspace
{
    public class ZoomModel
    {
        public const int kMinimumZoom = 30; // 30% - 300% matches FireFox
        public const int kMaximumZoom = 300;

        private int _zoom = 100;

        public int Zoom
        {
            get { return _zoom; }
            set
            {
                var newValue = Math.Min(Math.Max(value, kMinimumZoom), kMaximumZoom);
                if (newValue == _zoom)
                    return;
                _zoom = newValue;
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler<EventArgs> ZoomChanged;
    }
}
