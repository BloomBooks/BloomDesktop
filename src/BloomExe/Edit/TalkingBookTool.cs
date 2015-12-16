namespace Bloom.Edit
{
	class TalkingBookTool : ToolboxTool
	{
		public const string StaticToolId = "talkingBook";  // Avoid changing value; see ToolboxTool.JsonToolId
		public override string ToolId { get { return StaticToolId; } }
	}
}
