using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Skybound.Gecko;

namespace Bloom.Edit
{
	/// <summary>
	/// Manages configuration UI and settings for templates that contain setup scripts
	/// </summary>
	public class Configurator
	{
		public static bool IsConfigurable(string folderPath)
		{
			//enhance: would make sense to just work with books, but setting up books in tests is currently painful.
			//When we make a class to make that easy, we should switch this.
			//BookStorage storage = new BookStorage(folderPath, null);
			//return (null != FindConfigurationPage(dom));

			return File.Exists(Path.Combine(folderPath, "configuration.htm"));
		}

		public  DialogResult ShowConfigurationDialog(string folderPath)
		{
			using (var dlg = new ConfigurationDialog(Path.Combine(folderPath, "configuration.htm")))
			{
				var result = dlg.ShowDialog(null);
				if(result == DialogResult.OK)
				{
					ConfigurationData = dlg.FormData;
				}
				return result;
			}
		}

		public void ConfigureBook(string bookPath)
		{
			/* setup jquery in chrome console (first open a local file):
			 * script = document.createElement("script");
				script.setAttribute("src", "http://ajax.googleapis.com/ajax/libs/jquery/1.4.2/jquery.min.js");

			 *
			 * Other snippets
			 *
			 * document.body.appendChild(script);
			 *
			 * alert(jQuery.parseJSON('{\"message\": \"triscuit\"}').message)
			 *
			 *
			 * alert($().jquery)
			 */
			var b = new Skybound.Gecko.GeckoWebBrowser();
			var neededToMakeThingsWork = b.Handle;
			b.Navigate(bookPath);
			Application.DoEvents();
			ConfigurationData = "{\"calendar\": {\"year\": \"2012\"}}";

			//Now we call the method which takes that confuration data and adds/removes/updates pages.
			//We have the data as json string, so first we turn it into object for the updateDom's convenience.
			RunJavaScript(b,"updateDom(jQuery.parseJSON('"+ConfigurationData+"'))");
			Application.DoEvents();

			//Ok, so we should have a modified DOM now, which we can save back over the top.
			//Debug.WriteLine(b.Document.DocumentElement.InnerHtml);
			b.SaveDocument(bookPath, "application/xhtml+xml");
		}

		public void RunJavaScript(GeckoWebBrowser b, string script)
		{
			b.Navigate("javascript:void(" + script + ")");
			Application.DoEvents(); //review... is there a better way?  it seems that NavigationFinished isn't raised.
		}

		/// <summary>
		/// A JSON string of the configuration
		/// </summary>
		public string ConfigurationData { get; set; }

	}
}
