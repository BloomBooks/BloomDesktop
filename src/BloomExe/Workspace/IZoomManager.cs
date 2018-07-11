using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Workspace
{
	/// <summary>
	/// Interface implemented by IBloomTabArea instances if they support zooming
	/// </summary>
	interface IZoomManager
	{
		// A percentage (expected to be a multiple of 10, at least 30) displayed in and controlled by
		// the control in the top right. Implementors are expected to persist the zoom factor for
		// their area and update the actual appearance when it changes.
		int Zoom { get; }
		void SetZoom(int zoom);
	}
}
