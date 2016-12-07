// Copyright (c) 2014-2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Reflection;
using NUnit.Framework;
using Bloom.MiscUI;

namespace BloomTests
{
	[TestFixture]
	[Category("RequiresUI")]
	public class ProblemReporterDialogTests
	{
		class ProblemReporterDialogDouble: ProblemReporterDialog
		{
			public ProblemReporterDialogDouble()
			{
				Success = true;
				_youTrackProjectKey = "AUT";

				this.Load += (sender, e) =>
				{
					_description.Text = "Created by unit test of " + Assembly.GetAssembly(this.GetType()).FullName;
					_okButton_Click(sender, e);
				};
			}

			public bool Success { get; private set; }

			protected override void UpdateDisplay()
			{
				if (_state == State.Success)
					Close();
			}

			protected override void ChangeState(State state)
			{
				if (state == State.CouldNotAutomaticallySubmit)
				{
					Success = false;
					Close();
				}
				base.ChangeState(state);
			}
		}

		/// <summary>
		/// This is just a smoke-test that will notify us if youtrack stops working with the API we're relying on.
		/// It sends reports to the AUT project on youtrack
		/// </summary>
		[Test]
		[Category("SkipOnTeamCity")] //I don't know why this is blocked, probably we need a firewall opening
		[Platform(Exclude = "Linux", Reason = "YouTrackSharp is too Windows-centric")]
		[STAThread]
		public void CanSubmitProblemReportTestProject()
		{
			using (var dlg = new ProblemReporterDialogDouble())
			{
				dlg.ShowDialog();
				Assert.That(dlg.Success, Is.True, "Automatic submission of issue failed");
			}
		}
	}
}
