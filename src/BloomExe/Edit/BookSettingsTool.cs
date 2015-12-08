using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gecko.DOM;

namespace Bloom.Edit
{
	class BookSettingsTool : ToolboxTool
	{
		public const string StaticToolId = "bookSettings";  // Avoid changing value; see ToolboxTool.JsonToolId
		public override string ToolId { get { return StaticToolId; } }

		internal override void SaveSettings(ElementProxy toolbox)
		{
			base.SaveSettings(toolbox);
			if (toolbox == null)
				return;
		}

	}
}
