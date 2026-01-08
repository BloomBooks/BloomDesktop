using System.Drawing;
using System.Windows.Forms;

namespace Bloom.CollectionTab
{
    public interface IBloomTabArea
    {
        string HelpTopicUrl { get; }
        Control TopBarControl { get; }
        int WidthToReserveForTopBarControl { get; }
        void PlaceTopBarControl();
        Bitmap ToolStripBackground { get; set; }
    }
}
