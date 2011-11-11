using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Palaso.Xml;
using Skybound.Gecko;
using Skybound.Gecko.DOM;

namespace Bloom.Edit
{
	public partial class ConfigurationDialog : Form
	{
		private readonly string _filePath;

		public ConfigurationDialog(string filePath)
		{
			_filePath = filePath;
			InitializeComponent();


		}

		private void ConfigurationDialog_Load(object sender, EventArgs e)
		{
			//_browser.WebBrowser.NavigateFinishedNotifier.NavigateFinished += new EventHandler(NavigateFinishedNotifier_NavigateFinished);
			_browser.Navigate(_filePath, false);
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
		   GeckoDocument doc = _browser.WebBrowser.Document;

			_browser.AddScriptSource("jquery-1.6.4.js");
			_browser.AddScriptSource("form2object.js");
			_browser.AddScriptContent(
				@"function gather()
					{
						var formData = form2object('form', '.', false, null);
						document.getElementById('output').innerHTML = JSON.stringify(formData, null, '\t');
					}");

			var body = doc.GetElementsByTagName("body").First();
			GeckoElement div = doc.CreateElement("div");
			div.Id = "output";
			body.AppendChild(div);

			_browser.RunJavaScript("gather()");

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
