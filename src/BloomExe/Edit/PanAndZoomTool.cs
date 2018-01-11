namespace Bloom.Edit
{
	public class PanAndZoomTool : ToolboxTool
	{
		public const string StaticToolId = "panAndZoom";  // Avoid changing value; see ToolboxTool.JsonToolId
		public override string ToolId => StaticToolId;
	}
}
