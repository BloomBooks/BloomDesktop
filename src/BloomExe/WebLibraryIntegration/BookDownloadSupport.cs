using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Collection;
using Microsoft.Win32;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// BookDownloadSupport is used by program.cs to set up and drive download requests from the web.
	/// </summary>
	public class BookDownloadSupport : IDisposable
	{
		private readonly DownloadOrderList _downloadOrders;
		private static Thread _serverThread;
		private static bool _shuttingDown;
		public delegate BookDownloadSupport Factory();//autofac uses this
		public const string ArgsPipeName = @"SendBloomArgs";

		public BookDownloadSupport(DownloadOrderList downloadOrders)
		{
			_downloadOrders = downloadOrders;
			// We need the download folder to exist if we are asked to download a book.
			// We also want it to exist, to show the (planned) link that offers to launch the web site.
			// Another advantage of creating it early is that we don't have to create it in the UI when we want to add
			// a downloaded book to the UI.
			// So, we just make sure it exists here at startup.
			string downloadFolder = BookTransfer.DownloadFolder;
			if (!Directory.Exists(downloadFolder))
			{
				var pathToSettingsFile = CollectionSettings.GetPathForNewSettings(Path.GetDirectoryName(downloadFolder),
					Path.GetFileName(downloadFolder));
				var settings = new NewCollectionSettings()
				{
					Language1Iso639Code = "en",
					Language1Name = "English",
					IsSourceCollection = true,
					PathToSettingsFile = pathToSettingsFile
					// All other defaults are fine
				};
				CollectionSettings.CreateNewCollection(settings);
			}

			// Make the OS run Bloom when it sees bloom://somebooktodownload
			RegisterForBloomUrlProtocol();

			// And if another bloom gets launched to deal with that download request, it will reach out to this
			// instance to do the downloading.

			// Start a server which can process requests from another instance of bloom
			// (typically started by initiating a download by navigating to a link starting with bloom://;
			// can also be by opening a bookorder file.)

			_serverThread = new Thread(ServerThreadAction);
			_serverThread.Start(_downloadOrders);
		}


		public bool HadOrder;

		public void HandleBloomBookDownloadOrder(string url)
		{
			HadOrder = true;
			_downloadOrders.AddOrder(url);
		}

		/// <summary>
		/// Make sure this instance is registered (at least for this user) and the program to handle bloom:// urls.
		/// See also where these registry entries are made by the wix installer (file Installer.wxs).
		/// </summary>
		private void RegisterForBloomUrlProtocol()
		{
			if (Palaso.PlatformUtilities.Platform.IsLinux)
			{
				// TODO-Linux: no idea what has to happen to register a url handler...probably not this, though.
				// See also where these registry entries are made by the wix installer (file Installer.wxs).
				return;
			}

			if (AlreadyRegistered(Registry.ClassesRoot))
				return;
			var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
			var key = root.CreateSubKey(@"bloom\shell\open\command");
			key.SetValue("", CommandToLaunchBloom);

			key = root.CreateSubKey("bloom");
			key.SetValue("", "BLOOM:URL Protocol");
			key.SetValue("URL Protocol", "");
		}

		private bool AlreadyRegistered(RegistryKey root)
		{
			var key = root.OpenSubKey(@"bloom\shell\open\command");
			if (key == null)
				return false;
			var wanted = CommandToLaunchBloom;
			if (wanted != (key.GetValue("") as string).ToLowerInvariant())
				return false;
			key = root.OpenSubKey("bloom");
			if (key.GetValue("") as string != "BLOOM:URL Protocol")
				return false;
			if (key.GetValue("URL Protocol") as string != "")
				return false;
			return true;
		}

		private string CommandToLaunchBloom
		{
			get { return Application.ExecutablePath.ToLowerInvariant() + " \"%1\""; }
		}

		private void StopReceivingArgsFromOtherBloom()
		{
			if (_serverThread != null)
			{
				_shuttingDown = true;
				// This will go to our own thread that is waiting for such a connection, allowing the WaitForConnection
				// to unblock so the thread can terminate.
				NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", ArgsPipeName, PipeDirection.Out);
				try
				{
					pipeClient.Connect(100);
				}
				catch (TimeoutException)
				{
					return; // failed to send, maybe we got a real request during shutdown?
				}
				_serverThread.Join(1000); // If for some reason we can't clean up nicely, we'll exit anyway
				_serverThread = null;
			}
		}

		/// <summary>
		/// The function executed by the server thread which listens for messages from other bloom instances.
		/// Review: do we need to be able to handle many at once? What will happen if the user opens one order while we are handling another?
		/// </summary>
		private static void ServerThreadAction(object downloadOrders)
		{
			while (!_shuttingDown)
			{
				var pipeServer = new NamedPipeServerStream(ArgsPipeName, PipeDirection.In);
				pipeServer.WaitForConnection();
				if (_shuttingDown)
					return; // We got the spurious message that allows us to unblock and exit
				string argument = null;
				try
				{
					int len = pipeServer.ReadByte() * 256;
					len += pipeServer.ReadByte();
					var inBuffer = new byte[len];
					pipeServer.Read(inBuffer, 0, len);
					argument = Encoding.UTF8.GetString(inBuffer);
				}
				catch (IOException e)
				{
					//Catch the IOException that is raised if the pipe is broken
					// or disconnected.
					// I think it is safe to ignore it...worst that happens is that whatever the other Bloom instance
					// was trying to do doesn't happen.
				}
				((DownloadOrderList)downloadOrders).AddOrder(argument);
				pipeServer.Dispose();
			}
		}

		public void Dispose()
		{
			StopReceivingArgsFromOtherBloom();
		}


	}
}
