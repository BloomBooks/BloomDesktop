using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using L10NSharp;

namespace Bloom.TeamCollection
{
	/// <summary>
	/// The types of messages and milestones that can be in the TeamCollectionMessageLog.
	/// In some ways it would be nicer to have distinct enums for MessageType and MilestoneType,
	/// but the union gives the possible values for the MessageType field of this class
	/// and the string that occurs in the file.
	/// </summary>
	public enum MessageAndMilestoneType
	{
		// Messages

		// something happened not worth changing team icon status
		// Corresponds to state Nominal
		History,
		// something new arrived (or a changed happened), user will be gently
		// encouraged to reload. The Reload button will be shown.
		NewStuff,
		// Errors of ordinary severity; user firmly encouraged to reload. The presence of a message of this
		// type will ensure that the Reload button is showing.
		Error,
		// Displays as Error, but it is not useful for user to reload, so that button should not show
		// (at least not because this message is in the log).
		ErrorNoReload,
		// Conflicting change error serious enough that current edits
		// will be clobbered (moved to Lost and Found); user strongly urged to reload.
		ClobberPending,

		// Milestones

		// We (Re)loaded the collection. Any errors in the process will
		// be reported (after this in the log), any new stuff picked up. Things are nominal
		// until something happens. Only NewStuff messages after a Reloaded
		// milestone cause Indicator state NewStuff.
		// (It's not entirely obvious whether the milestone should be before or after
		// anything that happens DURING the reload. For errors reported during the reload,
		// it would be somewhat nicer if it came after. For history, I'm not sure it matters.
		// However, it's possible new changes arrive DURING the reload but after we captured
		// a list of files to process, so it's desirable that NewStuff messages from events
		// during the reload should come after it. If we could be sure the system will never
		// process an idle event during reload, we could put it at the end, because then
		// any events during it would not be posted until afterwards. But I'm NOT sure of that.)
		Reloaded,
		// The user has seen (or is reasonably presumed to have seen)
		// any current errors. Only Error messages after this milestone
		// (and after Reloaded) cause Indicator state Error.
		// (If there are errors during a reload, they will occur AFTER the reload
		// milestone, but a LogDisplayed milestone will be placed AFTER them,
		// since they are displayed immediately.)
		LogDisplayed,
		// We have displayed the dialog indicating that the book has been
		// clobbered. Only ClobberPending messages after this (and after
		// Reloaded) cause Indicator state ClobberPending (if we ever
		// support that...current plans are to show ClobberPending using
		// a toast).
		ShowedClobbered
	}

	/// <summary>
	/// One message (or milestone) in TeamCollectionMessageLog.
	/// </summary>
	public class TeamCollectionMessage
	{
		public TeamCollectionMessage()
		{
		}

		public TeamCollectionMessage(MessageAndMilestoneType messageType, string l10nId, string rawEnglishMessageTemplate, string param0 = null, string param1 = null)
		{
			MessageType = messageType;
			When = DateTime.UtcNow;
			L10NId = l10nId;
			RawEnglishMessageTemplate = rawEnglishMessageTemplate;
			Param0 = param0;
			Param1 = param1;
		}

		public DateTime When { get; set; }
		public MessageAndMilestoneType MessageType { get; set; }
		public string L10NId { get; set; }
		// Possibly containing {0} and {1}, which will be replaced with Param0 and Param1.
		// The string corresponding to L10NId in the xlf, if any, wins, even in English.
		public string RawEnglishMessageTemplate { get; set; }
		public string Param0 { get; set; }
		public string Param1 { get; set; }

		// Tab-delimited format:
		// TIME TYPE StringId EnglishMessageWithParameterPlaceholders Param0 Param1
		// Milestones only have the first two.
		public string ToPersistedForm =>
			When.ToString("o") // ISO 8601
			+ "\t" + MessageType + "\t" + (L10NId ?? "") + "\t" + (RawEnglishMessageTemplate ?? "") + "\t" +
			(Param0 ?? "") + "\t" + (Param1 ?? "");

		public string TextForDisplay
		{
			get
			{
				var leadIn = DateTime.Now.ToShortDateString() + ": ";
				if (String.IsNullOrEmpty(RawEnglishMessageTemplate))
				{
					switch (MessageType)
					{
						// Review: need localization, unless maybe we just want to show an icon?
						// Or perhaps some or all don't need to show in the log at all?
						case MessageAndMilestoneType.Reloaded: return null;
						case MessageAndMilestoneType.LogDisplayed:
							return null; // review: or show nothing at all??
						case MessageAndMilestoneType.ShowedClobbered:
							return leadIn + "Repaired conflict"; // eventually remove when adequately conveyed in error message
					}
				}

				var msg = LocalizationManager.GetString(L10NId, RawEnglishMessageTemplate);
				return leadIn + string.Format(msg, Param0, Param1);
			}
		}

		public static TeamCollectionMessage FromPersistedForm(string form)
		{
			var parts = form.Split('\t');
			var result = new TeamCollectionMessage();
			if (parts.Length < 2)
			{
				return null;
			}
			if (DateTime.TryParse(parts[0], out var when))
			{
				result.When = when;
			}
			else
			{
				return null;
			}

			if (Enum.TryParse(parts[1], out MessageAndMilestoneType messageType))
			{
				result.MessageType = messageType;
			}
			else
			{
				return null;
			}

			// Probably overkill; if it has more than two it probably has them all.
			if (parts.Length > 5)
			{
				result.Param1 = parts[5];
			}

			if (parts.Length > 4)
			{
				result.Param0 = parts[4];
			}

			if (parts.Length > 3)
			{
				result.RawEnglishMessageTemplate = parts[3];
			}

			if (parts.Length > 2)
			{
				result.L10NId = parts[2];
			}
			return result;
		}
	}
}
