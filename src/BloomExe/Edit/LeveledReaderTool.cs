using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Edit
{
	class LeveledReaderTool : ToolboxTool
	{
		public const string ToolId = "leveledReader";  // Avoid changing value; see ToolboxTool.JsonToolId
		public override string JsonToolId { get { return ToolId; } }
	}
}
