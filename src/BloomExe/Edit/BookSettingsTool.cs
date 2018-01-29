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
