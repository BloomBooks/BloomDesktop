using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Palaso.Xml;

namespace Bloom.Edit
{
	/// <summary>
	/// Manages configuration UI and settings for templates that contain setup scripts
	/// </summary>
	public class Configurator
	{
		public static bool IsConfigurable(XmlDocument dom)
		{
			//enhance: would make sense to just work with books, but setting up books in tests is currently painful.
			//When we make a class to make that easy, we should switch this.
			//BookStorage storage = new BookStorage(folderPath, null);
			return (null != FindConfigurationPage(dom));
		}

		private static XmlNode  FindConfigurationPage(XmlDocument dom)
		{
			return dom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, '-bloom-configurationPage')]");
		}


		public static DialogResult ShowConfigurationDialog(string path)
		{
			using (var dlg = new ConfigurationDialog(path))
			{
				return dlg.ShowDialog(null);
			}
		}
	}
}
