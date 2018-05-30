using System;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish.AccessibilityChecker;
using Bloom.Publish.Epub;

namespace Bloom.web.controllers
{
	public class AccessibilityCheckApi
	{
		
		private readonly NavigationIsolator _isolator;
		private readonly BookServer _bookServer;
		private readonly BookSelection _bookSelection;
		public const string kApiUrlPart = "accessibilityCheck/";
		private AccessibilityCheckWindow _accessibilityCheckerWindow = null;


		public AccessibilityCheckApi(Bloom.Publish.AccessibilityChecker.AccessibilityCheckWindow.Factory createAccessibilityChecker)
		{
			// TODO: call this from autofac setup somehow
			AccessibilityCheckWindow.StaticSetFactory(createAccessibilityChecker);
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "showAccessibilityChecker", request =>
			{
				AccessibilityCheckWindow.StaticShow();
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart+"audioForAllText", request =>
			{
				request.ReplyWithText("not implemented");
			}, false);
			server.RegisterEndpointHandler(kApiUrlPart + "descriptionsForAllImages", request =>
			{
				request.ReplyWithText("not implemented");
			}, false);
			server.RegisterEndpointHandler(kApiUrlPart + "audioForAllImageDescriptions", request =>
			{
				request.ReplyWithText("not implemented");
			}, false);
		}
	}
}
