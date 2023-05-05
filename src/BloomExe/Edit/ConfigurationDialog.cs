using System;
using System.Text;
using System.Windows.Forms;
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
		public ConfigurationDialog(string configurationHtmlPath, string libraryJsonData)
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

		private async void  _okButton_ClickAsync(object sender, EventArgs e)
		{
			FormData = await _browser.RunJavaScriptAsync("gatherSettings()");
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
}
