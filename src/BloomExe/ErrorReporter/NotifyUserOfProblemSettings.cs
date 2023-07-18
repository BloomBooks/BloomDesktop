using System;
using System.Diagnostics;

namespace Bloom.ErrorReporter
{
	public class NotifyUserOfProblemSettings
	{
		#region public Settings API
		public AllowSendReport AllowSendReport { get; private set; } = AllowSendReport.AllowIfException;

		public string ExtraButtonLabel { get; private set; } = null;
		public Action<string, Exception> OnExtraButtonClicked { get; private set; } = null;
		#endregion

		// Constructors should only allow the valid parameter combinations
		#region Constructors
		/// <summary>
		/// Creates a Settings object with the default settings
		/// </summary>
		public NotifyUserOfProblemSettings()
		{
		}

		public NotifyUserOfProblemSettings(AllowSendReport allowSendReport)
		{
			this.AllowSendReport = allowSendReport;
		}

		public NotifyUserOfProblemSettings(string extraButtonLabel, Action<string, Exception> onExtraButtonClicked)
		{
			Debug.Assert(!(String.IsNullOrEmpty(extraButtonLabel) && onExtraButtonClicked != null),
				"onExtraButtonClicked parameter was provided, but extraButtonLabel is not provided and thus the click handler is useless"
			);

			this.ExtraButtonLabel = extraButtonLabel;
			this.OnExtraButtonClicked = onExtraButtonClicked;
		}

		public NotifyUserOfProblemSettings(AllowSendReport allowSendReport, string extraButtonLabel, Action<string, Exception> onExtraButtonClicked)
			: this(extraButtonLabel, onExtraButtonClicked)
		{
			this.AllowSendReport = allowSendReport;
		}
		#endregion
	}
}
