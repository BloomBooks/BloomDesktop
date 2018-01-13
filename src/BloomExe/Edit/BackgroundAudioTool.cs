using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Edit
{
	// The required subclass to make the background audio (music) toolbox work. No interesting C# behavior.
	class BackgroundAudioTool : ToolboxTool
	{
		public const string StaticToolId = "backgroundAudio"; // Avoid changing value; see ToolboxTool.JsonToolId
		public override string ToolId => StaticToolId;
	}
}
