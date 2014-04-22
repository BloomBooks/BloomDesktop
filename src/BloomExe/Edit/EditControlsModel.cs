using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.Edit
{
    public class EditControlsModel
    {
        private const string _sortIconSelectedClass = "sortIconSelected"; // The class we apply to the selected sort icon
        private const string _disabledIconClass = "disabledIcon"; // The class we apply to icons that are disabled.

        public delegate EditControlsView Factory();//autofac uses this

        int _stage;
        private int _level;
        private SortType _sort;

        internal List<Stage> Stages = new List<Stage>(); 

        public EditControlsModel()
        {
            // Enhance JohnT: eventually we probably persist somewhere what stage or level they are at?
            _stage = 1; // make consistent with normal state of HTML. Don't try to update or read the HTML at this point, browser is probably not ready.
            _level = 1;

            // Enhance Phil Hopper (JohnT): should load real Synphony wordlists appropriate to this language
            // Review JohnH (JohnT): what should it do if Synphony has not been configured?
            // Temporary, for testing
            AddStage("cat sat mat fat the on rat");
            AddStage("ate a lot hot cot dot");
            AddStage("at this stage the child can read lots and lots of words including some long words that may cause layout problems" +
                "if things go as anticipated or something unanticipated happens causing me to become discombobulated." +
                "here are some more words so we can see some high frequency ones");

            //Todo: figure how many there should be; what determines it, how is it configured?
            // Setting it here is just for testing.
            NumberOfLevels = 3;
        }

        /// <summary>
        /// The view should call this when the browser has loaded the page.
        /// It updates various things in the UI to be consistent with the state of things.
        /// </summary>
        internal void PostNavigationInitialize()
        {
            UpdateWordList();
            UpdateNumberOfStages();
            EnableStageButtons();
            EnableLevelButtons();
        }

        /// <summary>
        /// Keeps track of how the Words in the Stage are sorted (and changes the display when updated)
        /// </summary>
        public SortType Sort
        {
            get { return _sort; }
            set
            {
                _sort = value;
                UpdateWordList();
                UpdateSortStatus();
            }
        }

        /// <summary>
        /// Makes the state of the sort buttons reflect which one is supposed to be selected.
        /// </summary>
        private void UpdateSortStatus()
        {
            UpdateSelectedStatus("sortAlphabetic", Sort == SortType.Alphabetic);
            UpdateSelectedStatus("sortLength", Sort == SortType.Length);
            UpdateSelectedStatus("sortFrequency", Sort == SortType.Frequency);
        }

        private void UpdateSelectedStatus(string eltId, bool isSelected)
        {
            SetPresenceOfClass(eltId, isSelected, _sortIconSelectedClass);
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
        /// Construct a 'stage' based on the words (and their frequencies) that occur in a string.
        /// This is a simple-minded version mainly useful for demonstration and testing.
        /// It doesn't handle internal punctuation in words, for example.
        /// </summary>
        /// <param name="words"></param>
        internal void AddStage(string words)
        {
            var stage = new Stage();
            stage.IncrementFrequencies(words);
            Stages.Add(stage);
        }

        /// <summary>
        /// The view for which we are the model.
        /// </summary>
        internal IEditControlsView View { get; set; }

        internal int StageNumber
        {
            get { return _stage; }
            set
            {
                if (value < 1 || value > Stages.Count)
                    return;
                _stage = value;
                UpdateElementContent("stageNumber", _stage.ToString(CultureInfo.InvariantCulture));
                UpdateWordList();
                EnableStageButtons();
            }
        }

        private void EnableStageButtons()
        {
            UpdateDisabledStatus("decStage", StageNumber <= 1);
            UpdateDisabledStatus("incStage", StageNumber >= Stages.Count);
        }

        private void UpdateDisabledStatus(string eltId, bool isDisabled)
        {
            SetPresenceOfClass(eltId, isDisabled, _disabledIconClass);
        }

        private void EnableLevelButtons()
        {
            UpdateDisabledStatus("decLevel", LevelNumber <= 1);
            UpdateDisabledStatus("incLevel", LevelNumber >= NumberOfLevels);
        }

        internal void UpdateNumberOfStages()
        {
            UpdateElementContent("numberOfStages", Stages.Count.ToString(CultureInfo.InvariantCulture));           
        }

        internal void UpdateWordList()
        {
            var wordFrequencies = Stages[StageNumber - 1].Words;
            // Enhance: so far this assumes case is significant if the input words have it.
            // If this is not so we may need to convert to LC before sorting (or before displaying the list?) (optionally?)
            // Also the sorts are currently based on the UI culture; may want to make it invariant or even support some kind
            // of configurable sort? Generally we won't have a Windows culture for a minority language, but we could allow the
            // the user to choose the most similar one.
            var words = new List<string>(wordFrequencies.Keys);
            switch (Sort)
            {
                case SortType.Alphabetic:
                    words.Sort();
                    break;
                case SortType.Length:
                    words.Sort((x,y) => x.Length == y.Length ?  x.CompareTo(y) : x.Length.CompareTo(y.Length));
                    break;
                case SortType.Frequency:
                    words.Sort((x, y) =>
                    {
                        var yFrequency = wordFrequencies[y];
                        var xFrequency = wordFrequencies[x];
                        if (xFrequency == yFrequency)
                            return x.CompareTo(y); // same frequency, make alphabetic
                        return yFrequency.CompareTo(xFrequency);  // purposely backwards, high freq first
                    });
                   break;
            }
            // Enhance JohnT: is there a smarter way to decide # columns? Maybe the HTML could be made to do it itself?
            // "Organize this list in as many columns as fit" feels like a common task, but a quick search didn't reveal
            // an obvious existing solution.
            // Review JohnH (JohnT): should they be arranged across rows or down columns?
            int wordsPerRow = 3;
            if (words.Count > 0)
            {
                int maxWordLength = (from w in words select w.Length).Max();
                if (maxWordLength > 9)
                    wordsPerRow = 2; // a crude way of improving layout.
            }
            int wordIndex = 0;
            var sb = new StringBuilder();
            foreach (var word in words)
            {
                if (wordIndex == 0)
                    sb.Append("<tr>");
                sb.Append("<td>");
                sb.Append(word);
                sb.Append("</td>");
                wordIndex++;
                if (wordIndex == wordsPerRow)
                {
                    wordIndex = 0;
                    sb.Append("</tr>");
                }
            }
            if (wordIndex != 0)
                sb.Append("</tr>");
            UpdateElementContent("wordList", sb.ToString());
        }

        internal int NumberOfLevels { get; set; }

        internal int LevelNumber
        {
            get { return _level; }
            set
            {
                if (value < 1)
                    return;
                _level = value;
                UpdateElementContent("levelNumber", _level.ToString(CultureInfo.InvariantCulture));
                EnableLevelButtons();
            }
        }

        /// <summary>
        /// Set the InnerHtml of the element identified by the ID. It must exist.
        /// Overridden in test stubs.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="val"></param>
        internal virtual void UpdateElementContent(string id, string val)
        {
            View.Browser.DomDocument.GetElementById(id).InnerHtml = val;
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
            return View.Browser.DomDocument.GetElementById(id).InnerHtml;
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
                case "decStage":
                    StageNumber--;
                    break;
                case "incStage":
                    StageNumber++;
                    break;
                case "incLevel":
                    LevelNumber++;
                    break;
                case "decLevel":
                    LevelNumber--;
                    break;
                case "sortLength":
                    Sort =EditControlsModel.SortType.Length;
                    break;
                case "sortFrequency":
                    Sort = EditControlsModel.SortType.Frequency;
                    break;
                case "sortAlphabetic":
                    Sort = EditControlsModel.SortType.Alphabetic;
                    break;
                case "setUpStages":
                    SetUpStages();
                    break;
            }
        }

        private void SetUpStages()
        {
            // Enhance Phil Hopper (JohnT): this should launch the real Synphony dialog (or some modified version of our own),
            // and then update the Stages list and call UpdateWordList(), UpdateNumberOfStages().
            MessageBox.Show(View as Control,
                "This dialog should be replaced with a Synphony one to configure word lists for stages",
                "Configure Stages");
        }

        /// <summary>
        /// A Stage represents one stage in the DecodableReader process.
        /// Currently it has a list of words, each with an associated frequency (presumably of occurrence
        /// in some interesting corpus).
        /// </summary>
        internal class Stage
        {
            public readonly Dictionary<string, int> Words = new Dictionary<string, int>();

            public void SetFrequency(string key, int val)
            {
                Words[key] = val;
            }

            /// <summary>
            /// A crude way to initialize a stage from one or more chunks of text containing space-delimited words.
            /// If we use this for more than demo purposes it needs to be smarter about
            /// identifying word boundaries and internal punctuation in words.
            /// </summary>
            /// <param name="input"></param>
            public void IncrementFrequencies(string input)
            {
                foreach (var word in input.Split(' '))
                {
                    var key = word;
                    foreach (var c in word.ToCharArray())
                    {
                        if (!Char.IsLetterOrDigit(c))
                            key = key.Replace(c.ToString(), "");
                    }
                    int count;
                    Words.TryGetValue(key, out count);
                    count++;
                    Words[key] = count;
                }
            }
        }

        /// <summary>
        /// Ways of sorting our list
        /// </summary>
        public enum SortType
        {
            Alphabetic,
            Length,
            Frequency
        }
    }
}
