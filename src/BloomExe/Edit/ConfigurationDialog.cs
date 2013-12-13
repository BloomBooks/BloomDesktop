using System;
using System.Linq;
using System.Windows.Forms;
using Gecko;

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
		public ConfigurationDialog(string configurationHtmlPath, string libraryJsonData)
		{
			_filePath = configurationHtmlPath;
			_libraryJsonData = libraryJsonData;
			InitializeComponent();
		}

		private void ConfigurationDialog_Load(object sender, EventArgs e)
		{
			this.Activated += new EventHandler(On_Activated);

			_browser.WebBrowser.NavigateFinishedNotifier.NavigateFinished += new EventHandler(NavigateFinishedNotifier_NavigateFinished);
		//	this fires, but leave us in a state withtout a cursor			_browser.WebBrowser.DocumentCompleted += new EventHandler(NavigateFinishedNotifier_NavigateFinished);

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

		void NavigateFinishedNotifier_NavigateFinished(object sender, EventArgs e)
		{
			_browser.AddScriptSource("jquery-1.6.4.js");
			_browser.AddScriptSource("form2object.js");
			_browser.AddScriptSource("js2form.js");
			_browser.AddScriptSource("underscore.js");

			_browser.AddScriptContent(
				@"function gatherSettings()
					{
						var formData = form2object('form', '.', false, null);
						document.getElementById('output').innerHTML = JSON.stringify(formData, null, '\t');
					}
				function preloadSettings()
					{
						 x =  "+_libraryJsonData+ @";
						var $inputs = $('#form').find('[name]');
						populateForm($inputs, x, 'name');
					}");

			//if we have saved data from a previous run, prepopulate the form with that

			_browser.RunJavaScript("preloadSettings()"); //nb: if this starts removing the defaults, it means we've lost the patch: if(valForForm != null) on line 80 of jsform.js
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
		   GeckoDocument doc = _browser.WebBrowser.Document;

			var body = doc.GetElementsByTagName("body").First();
			GeckoElement div = doc.CreateElement("div");
			div.Id = "output";
			body.AppendChild(div);

			_browser.RunJavaScript("gatherSettings()");

			FormData = div.InnerHtml;
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
