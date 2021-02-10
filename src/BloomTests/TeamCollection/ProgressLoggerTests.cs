using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.TeamCollection;
using Bloom.web;
using Gecko.WebIDL;
using NUnit.Framework;
using SIL.IO;
using SIL.Progress;

namespace BloomTests.TeamCollection
{
	public class ProgressLoggerTests
	{
		private TempFile _logFile;
		private ProgressLogger _logger;
		private ProgressSpy _progressSpy;
		private string[] _lines;
		[OneTimeSetUp]
		public void OneTimeSetup()
		{
			_logFile = new TempFile();
			_progressSpy = new ProgressSpy();
			_logger = new ProgressLogger(_logFile.Path, _progressSpy);

			_logger.MessageWithoutLocalizing("First Message", MessageKind.Progress);
			_logger.Message("", "", "Second Message is success!", MessageKind.Progress);
			_logger.Message("", "", "You have been warned: stop it, or else!", MessageKind.Warning);

			// Make sure it appends
			_logger.Dispose();
			_logger = new ProgressLogger(_logFile.Path, _progressSpy);

			_logger.Message("", "", "You've done it now! I warned you", MessageKind.Error);
			_logger.Log("This should only be in the file");
			_logger.Dispose();

			_lines = RobustFile.ReadAllLines(_logFile.Path);
			_logFile.Dispose();
		}

		[Test]
		public void GotFirstMessage()
		{
			Assert.That(_progressSpy.Messages[0].Item1, Is.EqualTo("First Message"));
			Assert.That(_progressSpy.Messages[0].Item2, Is.EqualTo(MessageKind.Progress));
			Assert.That(_lines[0], Is.EqualTo("progress:"));
			Assert.That(_lines[1], Is.EqualTo("\tFirst Message"));
		}

		[Test]
		public void GotSecondMessage()
		{
			var secondMessage = "Second Message is success!";
			Assert.That(_progressSpy.Messages[1].Item1, Is.EqualTo(secondMessage));
			Assert.That(_progressSpy.Messages[1].Item2, Is.EqualTo(MessageKind.Progress));
			Assert.That(_lines[2], Is.EqualTo("progress:"));
			Assert.That(_lines[3], Is.EqualTo("\t" + secondMessage));
		}

		[Test]
		public void GotWarning()
		{
			var warning = "You have been warned: stop it, or else!";
			Assert.That(_progressSpy.Messages[2].Item1, Is.EqualTo(warning));
			Assert.That(_progressSpy.Messages[2].Item2, Is.EqualTo(MessageKind.Warning));
			Assert.That(_lines[4], Is.EqualTo("warning:"));
			Assert.That(_lines[5], Is.EqualTo("\t" + warning));
		}

		[Test]
		public void GotError()
		{
			var error = "You've done it now! I warned you";
			Assert.That(_progressSpy.Messages[3].Item1, Is.EqualTo(error));
			Assert.That(_progressSpy.Messages[3].Item2, Is.EqualTo(MessageKind.Error));
			Assert.That(_lines[6], Is.EqualTo("error:"));
			Assert.That(_lines[7], Is.EqualTo("\t" + error));
		}

		[Test]
		public void GotLogMessage()
		{
			Assert.That(_progressSpy.Messages.Count, Is.EqualTo(4), "Should not have sent Log message to inner progress");
			Assert.That(_lines[8], Is.EqualTo("progress:"));
			Assert.That(_lines[9], Is.EqualTo("\t" + "This should only be in the file"));
		}

	}
}
