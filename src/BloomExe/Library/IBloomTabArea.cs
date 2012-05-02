using System.Windows.Forms;

namespace Bloom
{
	public interface IBloomTabArea
	{
		string HelpTopicUrl { get;}
		Control TopBarControl { get; }
	}
}