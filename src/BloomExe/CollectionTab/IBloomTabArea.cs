using System.Windows.Forms;

namespace Bloom.CollectionTab
{
	public interface IBloomTabArea
	{
		string HelpTopicUrl { get;}
		Control TopBarControl { get; }
	}
}