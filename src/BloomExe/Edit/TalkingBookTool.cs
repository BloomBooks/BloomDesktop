using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gecko.DOM;

namespace Bloom.Edit
{
	class TalkingBookTool : AccordionTool
	{
		private bool _showRecordingtools;
		public const string TBName = "talkingBook";
		public override string Name { get { return TBName; } }

		internal override void SaveSettings(EditingView _view)
		{
			base.SaveSettings(_view);
			var accordion = _view.Browser.WebBrowser.Window.Document.GetElementById("accordion") as GeckoIFrameElement;
			if (accordion == null)
				return;
			var recordingCheckBox = accordion.ContentDocument.GetElementById("showRecordingTools") as GeckoInputElement;
			_showRecordingtools = recordingCheckBox != null && recordingCheckBox.Checked;
		}

		internal override void RestoreSettings(EditingView _view)
		{
			base.RestoreSettings(_view);
			// Does not work, because changing the state of the check box by javascript does not trigger the change function.
			//var recordingCheckBox = _view.GetShowRecordingToolsCheckbox();
			//if (recordingCheckBox != null)
			//	recordingCheckBox.Checked = _showRecordingtools;
			if (_showRecordingtools)
				_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.showRecordingControls(); }");
		}
	}
}
