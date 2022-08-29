using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Gecko;
using Gecko.WebIDL;
using SIL.IO;

namespace Bloom.Edit
{
	public partial class ConfigurationDialog : Form
	{
		private readonly string _filePath;
		private readonly string _libraryJsonData;

		/// <summary>
		/// Show an html form, prepopulating it with saved values, and returning the new values when they click "OK"
		/// </summary>
		/// <param name="configurationHtmlPath"></param>
		/// <param name="libraryJsonData">Values saved previously</param>
		public ConfigurationDialog(string configurationHtmlPath, string libraryJsonData, NavigationIsolator isolator)
		{
			_filePath = configurationHtmlPath;
			_libraryJsonData = libraryJsonData;
			InitializeComponent();
		}

		private void ConfigurationDialog_Load(object sender, EventArgs e)
		{
			this.Activated += new EventHandler(On_Activated);

			// Update the configuration.html file, which typically was copied into
			// the book folder as we created a new book, to make sure it has script
			// elements for the libraries we need to configure the HTML,
			// and also a chunk of code that is set up to contain the JSON data
			// from making earlier configurable books, and to load that data into
			// the dialog after the main HTML loads.
			var configuration = RobustFile.ReadAllText(_filePath, Encoding.UTF8);
			var newConfig = Configurator.SetupConfigurationHtml(configuration, _libraryJsonData);
			RobustFile.WriteAllText(_filePath, newConfig, Encoding.UTF8);

			_browser.Navigate(_filePath, false);
		}

		private void On_Activated(object sender, EventArgs e)
		{
			/* The problem this solves is that when you're switching between this dialog and some
			 * other application (don't know why, but people did it)... when you come back, the browser would
			 * be all confused and sometimes you couldn't type at all.
			 * So now, when we come back to Bloom (this activated event), we *deselect* the browser, then reselect it, and it's happy.
			 */

			_okButton.Select();
			_browser.Select();
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			FormData =_browser.RunJavaScript("gatherSettings()");
			DialogResult = DialogResult.OK;
			Close();

		}

		/// <summary>
		/// A JSON string of the form
		/// </summary>
		public string FormData { get; set; }

		private void _cancelButton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}
	}


	/*DOM.GeckoScriptElement script = Document.CreateElement("script").AsScriptElement();
script.Type = "text/javascript";
script.Text = "function doAlert(){ alert('My alert - fired by automating a button click on the [Automated Button]'); }";
Document.Body.AppendChild(script);

script = Document.CreateElement("script").AsScriptElement();
script.Type = "text/javascript";
script.Text = "function callDoAlert(id){ var el = document.getElementById(id); el.click(); }";
Document.Body.AppendChild(script);

DOM.GeckoInputElement button = Document.CreateElement("input").AsInputElement();
button.Type = "button";
button.Id = "myButton";
button.Value = "Automated Button";
button.SetAttribute("onclick", "javascript:doAlert();");

Document.Body.AppendChild(button);

DOM.GeckoInputElement button2 = Document.CreateElement("input").AsInputElement();
button2.Type = "button";
button2.Id = "myOtherButton";
button2.Value = "Press Me";
button2.SetAttribute("onclick", "javascript:document.getElementById('myButton').click();");

Document.Body.AppendChild(button2);

//uncomment to fully automate without the <webbrowser>.Navigate("javascript:.."); hack
//button2.click();
*/
}
