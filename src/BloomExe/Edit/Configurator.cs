using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using Palaso.Code;
using Palaso.Extensions;
using Skybound.Gecko;

namespace Bloom.Edit
{
	/// <summary>
	/// Manages configuration UI and settings for templates that contain setup scripts
	/// </summary>
	public class Configurator
	{
		private readonly string _folderInWhichToReadAndSaveProjectSettings;

		public Configurator(string folderInWhichToReadAndSaveProjectSettings)
		{
			_folderInWhichToReadAndSaveProjectSettings = folderInWhichToReadAndSaveProjectSettings;
			PathToProjectJson = _folderInWhichToReadAndSaveProjectSettings.CombineForPath("configuration.txt");
			RequireThat.Directory(folderInWhichToReadAndSaveProjectSettings).Exists();
			LocalData = string.Empty;
		}

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
			using (var dlg = new ConfigurationDialog(Path.Combine(folderPath, "configuration.htm"), GetProjectData()))
			{
				var result = dlg.ShowDialog(null);
				if(result == DialogResult.OK)
				{
					CollectJsonData(dlg.FormData);
				}
				return result;
			}
		}

		/// <summary>
		/// Before calling this, ConfigurationData has to be loaded. E.g., by running ShowConfigurationDialog()
		/// </summary>
		/// <param name="bookPath"></param>
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

			//Now we call the method which takes that confuration data and adds/removes/updates pages.
			//We have the data as json string, so first we turn it into object for the updateDom's convenience.
			RunJavaScript(b,"updateDom(jQuery.parseJSON('"+GetAllData()+"'))");
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

		public string LocalData { get; set; }

		private string PathToProjectJson { get; set; }

		/// <summary>
		/// Saves off the project part to disk, stores the rest
		/// </summary>
		/// <param name="newDataString"></param>
		public void CollectJsonData(string newDataString)
		{
			if (string.IsNullOrEmpty(newDataString))
			{
				LocalData = "";
				return;
			}
			dynamic newData = DynamicJson.Parse(newDataString);
			dynamic projectData=null;
			if(newData.IsDefined("project"))
			{
				projectData = newData.project;
			}
			//Now in LocalData, we want to save everything that isn't project data
			newData.Delete("project");
			LocalData = newData.ToString();
			if (projectData == null)
				return;	//no project data in there, so we don't have anything to merge/save

			var existingDataString = GetProjectData();
			if (!string.IsNullOrEmpty(existingDataString))
			{
				dynamic existingData = DynamicJson.Parse(existingDataString);
				projectData = MergeJsonData(existingData.project.ToString(), projectData.ToString());
			}

			File.WriteAllText(PathToProjectJson, projectData.ToString());


		}


		/// <summary>
		/// merge the existing data with this new stuff
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b">b has priority</param>
		/// <returns></returns>

		private string MergeJsonData(string a, string b)
		{
			//NB: this has got to be the ugliest code I have written since HighSchool.  There are just all these weird bugs, missing functions, etc. in the
			//json libraries. And probably better ways to do this stuff, too.  Maybe it doesn't help that I'm using too different libaries in one function!
			//All I can say is it has unit test converage.
			JObject existing = JObject.Parse(a.ToString());
			foreach (KeyValuePair<string, dynamic> item in DynamicJson.Parse(b))
			{
				bool inExisting = Contains(existing, item.Key);
				if (IsComplexObject(item.Value.ToString()))
				{
					if (inExisting)
					{
						string merged = MergeJsonData(existing[item.Key].ToString().Replace("\r\n", "").Replace("\\", ""), item.Value.ToString());
						existing.Remove(item.Key);
						existing.Add(item.Key, JToken.Parse(merged));
					}
					else
					{
						existing.Add(item.Key, item.Value.ToString());
					}
				}
				else
				{
					if (inExisting)
					{
						existing.Remove(item.Key);
					}
					//JToken t = JToken.Parse(item.Value.ToString());
					if (item.Value is DynamicJson && item.Value.IsArray)
					{
						var value = JArray.Parse(item.Value.ToString());
						existing.Add(item.Key, value);
					}
					else
					{
						existing.Add(item.Key, item.Value);
					}
				}
			}
			return existing.ToString().Replace("\r\n", "").Replace("\\", "");
		}

		private bool IsComplexObject(string value)
		{
			//TODO this is just pretend
			return value.Contains(":");
		}
		private bool IsArray(string value)
		{
			//TODO this is just pretend
			return value.Contains("[");
		}

		private bool Contains(JObject o, string key)
		{
			JToken v;
			return o.TryGetValue(key, out v);
		}

		public string GetProjectData()
		{
			if(!File.Exists(PathToProjectJson))
				return string.Empty;

			var s= File.ReadAllText(PathToProjectJson);
			if(string.IsNullOrEmpty(s))
				return string.Empty;

			return "{\"project\": " + s + "}";
		}

		public string GetAllData()
		{
			string projectData = GetProjectData();
			var local = GetInnerjson(LocalData);
			var project = GetInnerjson(projectData);
			if(!string.IsNullOrEmpty(projectData))
				return "{"+local+", "+ project+"}";
			else
			{
				return LocalData;
			}
		}
		private string GetInnerjson(string json)
		{

			//this hack must die!  (trim by itself will take off as many }'s as it finds
			json = json.Replace("}}", "@@}");
			json = json.Replace("}}", "@@}");
			json = json.Replace("}}", "@@}");
			json = json.Replace("}}", "@@}");
			json = json.Trim(new char[] { '{', '}' });
			json = json.Replace("@@", "}");
			json = json.Replace("@@", "}");
			json = json.Replace("@@", "}");
			json = json.Replace("@@", "}");
			return json;
		}
	}
}
