using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Edit
{
	class DecodableReaderTool : ToolboxTool
	{
		public const string ToolId = "decodableReader"; // Avoid changing value; see AccordionToo.JsonToolId
		public override string JsonToolId { get { return ToolId; } }
	}
}
