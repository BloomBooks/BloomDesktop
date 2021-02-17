using Bloom.Api;
using Bloom.ErrorReporter;
using Bloom.MiscUI;
using Moq;
using System;
using System.Windows.Forms;

namespace BloomTests.DataBuilders
{
	// Uses a DataBuilder pattern to facilitate creation of an HtmlErrorReporter object
	class HtmlErrorReporterBuilder
	{
		private HtmlErrorReporter _reporter = HtmlErrorReporter.Instance;

		public HtmlErrorReporter Build()
		{
			return _reporter;
		}

		/// <summary>
		/// Provides reasonable default values for an HtmlErrorReporter that would be used in unit tests
		/// </summary>
		public HtmlErrorReporterBuilder WithTestValues()
		{
			BrowserDialogFactory(new Mock<IBrowserDialogFactory>().Object);

			var mockBloomServer = new Mock<IBloomServer>();
			BloomServer(mockBloomServer.Object);

			var testControl = new Control();
			testControl.CreateControl();
			Control(testControl);

			return this;
		}

		public HtmlErrorReporterBuilder BrowserDialogFactory(IBrowserDialogFactory factory)
		{
			_reporter.BrowserDialogFactory = factory;
			return this;
		}

		public HtmlErrorReporterBuilder BloomServer(IBloomServer bloomServer)
		{
			_reporter.BloomServer = bloomServer;
			return this;
		}

		public HtmlErrorReporterBuilder Control(Control control)
		{
			_reporter.Control = control;
			return this;
		}
	}
}
