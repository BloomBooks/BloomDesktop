namespace Bloom.Edit
{
	class TalkingBookTool : ToolboxTool
	{
		private bool _showRecordingtools;
		public const string StaticToolId = "talkingBook";  // Avoid changing value; see ToolboxTool.JsonToolId
		public override string ToolId { get { return StaticToolId; } }

		internal override void SaveSettings(ElementProxy toolbox)
		{
			base.SaveSettings(toolbox);
			if (toolbox == null)
				return;

			_showRecordingtools = toolbox.GetElementById("showRecordingTools").Checked;
		}

		internal override void RestoreSettings(EditingView _view)
		{
			base.RestoreSettings(_view);
			// Does not work, because changing the state of the check box by javascript does not trigger the change function.
			//var recordingCheckBox = _view.GetShowRecordingToolsCheckbox();
			//if (recordingCheckBox != null)
			//	recordingCheckBox.Checked = _showRecordingtools;
			if (_showRecordingtools)
				_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.showTalkingBookTool(); }");
		}
	}
}
