using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Bloom.ToPalaso.Experimental;
using Bloom.Api;
using Bloom.Book;
using Newtonsoft.Json.Linq;
using SIL.Code;
using SIL.Extensions;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Reporting;
using SIL.Xml;
using Bloom.SafeXml;

namespace Bloom.Edit
{
    /// <summary>
    /// Manages configuration UI and settings for templates that contain setup scripts
    /// </summary>
    public class Configurator
    {
        private readonly string _folderInWhichToReadAndSaveCollectionSettings;
        public delegate Configurator Factory(string folderInWhichToReadAndSaveCollectionSettings); //autofac uses this

        public Configurator(string folderInWhichToReadAndSaveCollectionSettings)
        {
            _folderInWhichToReadAndSaveCollectionSettings =
                folderInWhichToReadAndSaveCollectionSettings;
            PathToCollectionJson = _folderInWhichToReadAndSaveCollectionSettings.CombineForPath(
                "configuration.txt"
            );
            RobustIO.RequireThatDirectoryExists(folderInWhichToReadAndSaveCollectionSettings);
            LocalData = string.Empty;
        }

        public static bool IsConfigurable(string folderPath)
        {
            //enhance: would make sense to just work with books, but setting up books in tests is currently painful.
            //When we make a class to make that easy, we should switch this.
            //BookStorage storage = new BookStorage(folderPath, null);
            //return (null != FindConfigurationPage(dom));

            return RobustFile.Exists(Path.Combine(folderPath, "configuration.html"));
        }

        public DialogResult ShowConfigurationDialog(string folderPath)
        {
            using (
                var dlg = new ConfigurationDialog(
                    Path.Combine(folderPath, "configuration.html"),
                    GetCollectionData()
                )
            )
            {
                var result = dlg.ShowDialog(null);
                if (result == DialogResult.OK)
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
            using (var dlg = new ProgressDialogForeground())
            {
                dlg.Text = L10NSharp.LocalizationManager.GetString(
                    "CollectionTab.ConfiguringBookMessage",
                    "Building..."
                );
                dlg.ShowAndDoWork((progress) => ConfigureBookInternal(bookPath));
            }
        }

        public static void AddScriptFile(SafeXmlDocument doc, string src)
        {
            var head = doc.SelectSingleNode("//head") as SafeXmlElement;
            if (
                head.SelectSingleNode($"./script[@type='text/javascript' and @src='{src}']") != null
            )
                return; // already have it
            var script = doc.CreateElement("script");
            script.SetAttribute("src", src);
            script.SetAttribute("type", "text/javascript");
            head.AppendChild(script);
        }

        public static void AddScriptText(SafeXmlDocument doc, string id, string text)
        {
            var head = doc.SelectSingleNode("//head") as SafeXmlElement;
            var script =
                head.SelectSingleNode($"./script[@type='text/javascript' and @id='{id}']")
                as SafeXmlElement;
            if (script == null)
            {
                script = doc.CreateElement("script");
                script.SetAttribute("type", "text/javascript");
                script.SetAttribute("id", id);
                head.AppendChild(script);
            }
            script.InnerText = text;
        }

        /// <summary>
        /// Passed the contents of the configuration.html file in the book's folder
        /// (typically copied from the template from which it was made)
        /// and the configuration.txt file in the parent collection, this yields an
        /// updated configuration.html content ready to use as the source of the
        /// ConfigurationDialog.
        /// </summary>
        /// <param name="html"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static string SetupConfigurationHtml(string html, string settings)
        {
            var doc = XmlHtmlConverter.GetXmlDomFromHtml(html);
            AddScriptFile(doc, "jquery-1.10.1.js");
            AddScriptFile(doc, "form2object.js");
            AddScriptFile(doc, "js2form.js");
            AddScriptFile(doc, "underscore.js");
            AddScriptText(
                doc,
                "configuredScript",
                @"function gatherSettings()
					{
						var formData = form2object('form', '.', false, null);
						return JSON.stringify(formData, null, '\t');
					}
				function preloadSettings()
					{
						 x =  "
                    + settings
                    + @";
						var $inputs = $('#form').find('[name]');
						populateForm($inputs, x, 'name');
					}
				window.addEventListener('load', preloadSettings)"
            );
            return XmlHtmlConverter.ConvertDomToHtml5(doc);
        }

        private async Task ConfigureBookInternal(string bookPath)
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

            var doc = XmlHtmlConverter.GetXmlDomFromHtmlFile(bookPath, false);
            XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(doc);
            XmlHtmlConverter.SaveDOMAsHtml5(doc, bookPath);

            using (var b = BrowserMaker.MakeBrowser())
            {
                var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(bookPath, false));
                b.NavigateAndWaitTillDone(dom, 30000, InMemoryHtmlFileSource.Nav);

                //Now we call the method which takes that confuration data and adds/removes/updates pages.
                //We have the data as json string, so first we turn it into object for the updateDom's convenience.
                await b.GetStringFromJavascriptAsync("runUpdate(" + GetAllData() + ")");

                //Ok, so we should have a modified DOM now, which we can save back over the top.

                //nice non-ascii paths kill this, so let's go to a temp file first
                var temp = TempFile.CreateAndGetPathButDontMakeTheFile(); //we don't want to wrap this in using
                await b.SaveDocumentAsync(temp.Path);
                RobustFile.Delete(bookPath);
                RobustFile.Move(temp.Path, bookPath);
            }

            var sanityCheckDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(bookPath, false);

            // Because the Mozilla code loaded the document from a filename initially, and we later save to a
            // different directory, Geckofx45's SaveDocument writes out the stylesheet links as absolute paths
            // using the file:// protocol markup. When we try to open the new file, Mozilla then complains
            // vociferously about security issues, and refuses to access the stylesheets as far as I can tell.
            // Eventually, several of the stylesheets are cleaned up by being added in again, but a couple of
            // them end up with invalid relative paths because they never get re-added.  So let's go through
            // all the stylesheet links here and remove everything except the bare filenames.
            // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3573 for what happens without this fix.
            foreach (
                SafeXmlElement link in sanityCheckDom.SafeSelectNodes("//link[@rel='stylesheet']")
            )
            {
                var href = link.GetAttribute("href");
                if (href.StartsWith("file://"))
                    link.SetAttribute(
                        "href",
                        Path.GetFileName(href.Replace("file:///", "").Replace("file://", ""))
                    );
            }
            XmlHtmlConverter.SaveDOMAsHtml5(sanityCheckDom, bookPath);

            //NB: this check only makes sense for the calendar, which is the only template we've create that
            // uses this class, and there are no other templates on the drawing board that would use it.
            // If/when we use this for something else, this
            //won't work. But by then, we should be using a version of geckofx that can reliably tell us
            //when it is done with the previous navigation.
            if (sanityCheckDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]").Length < 24) //should be 24 pages
            {
                Logger.WriteMinorEvent(RobustFile.ReadAllText(bookPath)); //this will come to us if they report it
                throw new ApplicationException(
                    "Malformed Calendar (code assumes only calendar uses the Configurator, and they have at least 24 pages)"
                );
            }

            //NB: we *want* exceptions thrown from the above to make it out.
        }

        public string LocalData { get; set; }

        private string PathToCollectionJson { get; set; }

        /// <summary>
        /// Saves off the Collection part to disk, stores the rest
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
            dynamic collectionData = null;
            // This is pulling in file data. Would prefer the field be named 'collection'
            // but that may affect older Bloom versions if we try to change it.
            if (newData.IsDefined("library"))
            {
                collectionData = newData.library; //a couple RuntimeBinderException errors are normal here, just keep going, it eventually gets past it.
            }
            //Now in LocalData, we want to save everything that isn't library data
            newData.Delete("library");
            LocalData = newData.ToString();
            if (collectionData == null)
                return; //no library data in there, so we don't have anything to merge/save

            var existingDataString = GetCollectionData();
            if (!string.IsNullOrEmpty(existingDataString))
            {
                DynamicJson existingData = DynamicJson.Parse(existingDataString);
                if (existingData.GetDynamicMemberNames().Contains("library"))
                    collectionData = MergeJsonData(
                        DynamicJson.Parse(existingDataString).library.ToString(),
                        collectionData.ToString()
                    );
            }

            RobustFile.WriteAllText(PathToCollectionJson, collectionData.ToString());
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
            //json libraries. And probably better ways to do this stuff, too.  Maybe it doesn't help that I'm using two different libaries in one function!
            //All I can say is it has unit test converage.
            JObject existing = JObject.Parse(a.ToString());
            foreach (KeyValuePair<string, dynamic> item in DynamicJson.Parse(b))
            {
                bool inExisting = Contains(existing, item.Key);
                if (IsComplexObject(item.Value.ToString()))
                {
                    if (inExisting)
                    {
                        string merged = MergeJsonData(
                            existing[item.Key].ToString().Replace("\r\n", "").Replace("\n", ""),
                            item.Value.ToString().Replace("\r\n", "").Replace("\n", "")
                        );
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
            // I don't know why an earlier version of this code wanted to drop backslashes.
            // But keeping them is essential to handling strings containing a double-quote
            // (e.g., for names in wall calendar)
            return existing.ToString().Replace("\r\n", "").Replace("\n", ""); //.Replace("\\", "");
        }

        private bool IsComplexObject(string value)
        {
            // This is actually a bit tricky. The value of a field in a Json string could be a list.
            // The objects in the list could be quite complex, but nevertheless, we don't know how
            // to merge lists as complex objects. Or, the value could be a simple string, but it
            // might look like a JSON object; for example, an early version of this method looked for
            // a colon in the value, but some orthographies use colons. Looking for a leading {
            // eliminates most of the non-complex things quickly, but that character could be in
            // an orthography, too. So if it initially looks complex, we actually verify that we
            // can parse it as an object. If not, we take it as non-complex.
            if (!value.Trim().StartsWith("{"))
                return false;
            try
            {
                JObject.Parse(value);
            }
            catch (JsonReaderException)
            {
                // However complex it may look, we can't handle digging deeper into it
                return false;
            }

            return true;
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

        public string GetCollectionData()
        {
            if (!RobustFile.Exists(PathToCollectionJson))
                return "{}"; //return "{\"dummy\": \"x\"}";//TODO

            var s = RobustFile.ReadAllText(PathToCollectionJson);
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            return "{\"library\": " + s + "}";
        }

        public string GetAllData()
        {
            string collectionData = GetCollectionData();
            var local = GetInnerjson(LocalData);
            var collection = GetInnerjson(collectionData);
            if (!string.IsNullOrEmpty(collection))
                return "{" + local + ", " + collection + "}";
            return LocalData;
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
