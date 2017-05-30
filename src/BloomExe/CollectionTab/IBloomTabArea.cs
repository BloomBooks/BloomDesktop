using System.Windows.Forms;
using System.Drawing;

namespace Bloom.CollectionTab
{
	public interface IBloomTabArea
	{
		string HelpTopicUrl { get;}
		Control TopBarControl { get; }
		int WidthToReserveForTopBarControl { get; }
		Bitmap ToolStripBackground { get; set; }
	}
}
