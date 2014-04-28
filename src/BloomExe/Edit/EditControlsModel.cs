using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Collection;

namespace Bloom.Edit
{
    public class EditControlsModel
    {
        public delegate EditControlsView Factory();//autofac uses this

        private CollectionSettings _collectionSettings;

        public EditControlsModel(CollectionSettings settings)
        {
            _collectionSettings = settings;
            // Enhance JohnT: eventually we probably persist somewhere what stage or level they are at?

        }

        /// <summary>
        /// Find the element with the indicated ID, and make sure that it has the className in its class attribute if isWanted is true, and not otherwise.
        /// (Tests currently assume it will be added last, but this is not required.)
        /// (class names used with this method should not occur as substrings within a longer class name)
        /// </summary>
        /// <param name="eltId"></param>
        /// <param name="isWanted"></param>
        /// <param name="className"></param>
        private void SetPresenceOfClass(string eltId, bool isWanted, string className)
        {
            var old = GetElementAttribute(eltId, "class");
            if (isWanted && ! old.Contains(className))
            {
                UpdateElementAttribute(eltId, "class", old + " " + className);
            }
            else if (!isWanted && old.Contains(className))
            {
                UpdateElementAttribute(eltId, "class", old.Replace(className, "").Replace("  ", " ").Trim());
            }
        }

        /// <summary>
        /// The view for which we are the model.
        /// </summary>
        internal IEditControlsView View { get; set; }

        /// <summary>
        /// Set the InnerHtml of the element identified by the ID. It must exist.
        /// Overridden in test stubs.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="val"></param>
        internal virtual void UpdateElementContent(string id, string val)
        {
            //View.Browser.DomDocument.GetElementById(id).InnerHtml = val;
        }

        /// <summary>
        /// Set the specified attribute of the specified object to the value.
        /// Overridden in test stubs.
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="attrName"></param>
        /// <param name="val"></param>
        internal virtual void UpdateElementAttribute(string elementId, string attrName, string val)
        {
            var elt = View.Browser.DomDocument.GetElementById(elementId);
            elt.SetAttribute(attrName, val);
        }

        /// <summary>
        /// Since this is being used on shipping HTML, we should not be asking for elements that don't exist.
        /// So it is deliberately allowed to crash if that happens.
        /// A missing attribute is allowed and will just return an empty string.
        /// Overridden in test stubs.
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="attrName"></param>
        /// <returns></returns>
        internal virtual string GetElementAttribute(string elementId, string attrName)
        {
            var elt = View.Browser.DomDocument.GetElementById(elementId);
            var attr = elt.Attributes[attrName];
            if (attr == null)
                return "";
            return attr.NodeValue ?? "";
        }

        /// <summary>
        /// Get the (HTML) content of the specified element. It must exist.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        internal virtual string GetElementContent(string id)
        {
            return ""; //View.Browser.DomDocument.GetElementById(id).InnerHtml;
        }

        /// <summary>
        /// Invoked when the user clicks on an element in the view with the specified id.
        /// (note: many ids are assigned for other reasons and have no click behavior.)
        /// </summary>
        /// <param name="id"></param>
        internal void ControlClicked(string id)
        {
            switch (id)
            {
 
                //case "setUpStages":
                //    SetUpStages();
                //    break;
            }
        }

        //private void SetUpStages()
        //{
        //    // Enhance Phil Hopper (JohnT): this should launch the real Synphony dialog (or some modified version of our own),
        //    // and then update the Stages list and call UpdateWordList(), UpdateNumberOfStages().
        //    MessageBox.Show(View as Control,
        //        "This dialog should be replaced with a Synphony one to configure word lists for stages",
        //        "Configure Stages");
        //}


        internal void PostNavigationInitialize()
        {
            // Backslashes don't seem to make it through whatever encoding/decoding process happens between here and the JavaScript.
            // Fortunately regular slashes are fine on both platforms.
            var path = _collectionSettings.DecodableLevelPathName.Replace('\\', '/');
#if DEBUG
            var fakeIt = "true";
#else
            var fakeIt = "false";
#endif
            RunJavaScript("initialize(\"" + path + "\", " + fakeIt + ")");
        }

        public void RunJavaScript(string script)
        {
            //NB: someday, look at jsdIDebuggerService, which has an Eval

            //TODO: work on getting the ability to get a return value: http://chadaustin.me/2009/02/evaluating-javascript-in-an-embedded-xulrunnergecko-window/ , EvaluateStringWithValue, nsiscriptcontext,  


            View.Browser.Navigate("javascript:void(" + script + ")");
            // from experimentation (at least with a script that shows an alert box), the script isn't run until this happens:
            //var filter = new TestMessageFilter();
            //Application.AddMessageFilter(filter);
            Application.DoEvents();


            //NB: Navigating and Navigated events are never raised. I'm going under the assumption for now that the script blocks    	
        }
    }
}
