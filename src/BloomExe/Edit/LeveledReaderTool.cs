using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Properties;

namespace Bloom.Edit
{
	class LeveledReaderTool : ToolboxTool
	{
		public const string StaticToolId = "leveledReader";  // Avoid changing value; see ToolboxTool.JsonToolId
		public override string ToolId { get { return StaticToolId; } }

		public override void SaveDefaultState()
		{
			base.SaveDefaultState();
			int level;
			if (Int32.TryParse(State, out level))
			{
				Settings.Default.CurrentLevel = level;
				Settings.Default.Save();
			}
		}

		public override string DefaultState()
		{
			return Settings.Default.CurrentLevel.ToString();
		}
	}
}
