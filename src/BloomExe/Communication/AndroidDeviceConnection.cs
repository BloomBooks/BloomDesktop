using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PodcastUtilities.PortableDevices;

namespace Bloom.Communication
{
	class AndroidDeviceConnection
	{
		private const string kBloomFolder = "Bloom";
		private IDevice _device;
		private string _bloomFolderPath;

		/// <summary>
		/// Attempt to establish a connection with a device which has the Bloom Reader app
		/// </summary>
		/// <param name="timeout">Number of seconds before the operation times out (optional - default is 10)</param>
		/// <returns>true if a connection was established, false otherwise</returns>
		public bool TryConnect(int timeout = 10)
		{
			var devices = EnumerateAllDevices();

			DateTime startTime = DateTime.Now;
			while (!devices.Any() && (DateTime.Now - startTime).Seconds < timeout)
			{
				Thread.Sleep(500);
				devices = EnumerateAllDevices();
			}

			if (!devices.Any())
				return false;

			_device = GetOneDevice(devices);

			return _device != null;
		}

		public bool BookExists(string fileName)
		{
			if (_device == null || _bloomFolderPath == null)
				throw new InvalidOperationException("Must connect before calling BookExists");

			return _device.GetObjectFromPath(Path.Combine(_bloomFolderPath, fileName)) != null;
		}

		public void SendBook(string bloomdPath)
		{
			if (_device == null)
				throw new InvalidOperationException("Must connect before calling SendBook");

			var sourceStream = File.OpenRead(bloomdPath);
			var targetStream = _device.OpenWrite(Path.Combine(_bloomFolderPath, Path.GetFileName(bloomdPath)), new FileInfo(bloomdPath).Length, true);
			Copy(sourceStream, targetStream);
		}

		public string GetDeviceName()
		{
			if (_device == null)
				throw new InvalidOperationException("Must connect before calling GetDeviceName");

			return _device.Name;
		}

		private void Copy(Stream source, Stream destination)
		{
			const int kCopyBufferSize = 262144;

			var buffer = new byte[kCopyBufferSize];

			while (true)
			{
				var readSize = source.Read(buffer, 0, kCopyBufferSize);
				if (readSize == 0)
				{
					break;
				}

				destination.Write(buffer, 0, readSize);
			}

			destination.Flush();
		}

		private static IEnumerable<IDevice> EnumerateAllDevices()
		{
			IDeviceManager manager = new DeviceManager();
			return manager.GetAllDevices();
		}

		/// <summary>
		/// Find the one with Bloom Reader installed (indicated by the presence of the Bloom directory
		/// as a direct child of a root storage object)
		/// </summary>
		/// <param name="devices"></param>
		/// <returns></returns>
		private IDevice GetOneDevice(IEnumerable<IDevice> devices)
		{
			IDevice theOneDevice = null;
			foreach (var device in devices)
			{
				_bloomFolderPath = GetBloomFolderPath(device);
				if (_bloomFolderPath != null)
				{
					if (theOneDevice != null)
						throw new ApplicationException(L10NSharp.LocalizationManager.GetString(
							"Publish.ReaderBookPublisher.MoreThanOneDevice", "More than one Android device with Bloom Reader is connected"));
					theOneDevice = device;
				}
			}
			return theOneDevice;
		}

		private string GetBloomFolderPath(IDevice device)
		{
			foreach (var rso in device.GetDeviceRootStorageObjects())
			{
				if (rso.GetFolders(kBloomFolder).Any())
				{
					return Path.Combine(rso.Name, kBloomFolder);
				}
			}
			return null;
		}
	}
}