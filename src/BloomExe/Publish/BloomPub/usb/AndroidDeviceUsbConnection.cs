#if !__MonoCS__
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using PodcastUtilities.PortableDevices;

namespace Bloom.Publish.BloomPub.usb
{
	enum DeviceNotFoundReportType
	{
		Unknown,
		NoDeviceFound,
		MoreThanOneReadyDevice
	}

	class OneReadyDeviceNotFoundEventArgs
	{
		public OneReadyDeviceNotFoundEventArgs(DeviceNotFoundReportType reportType, List<string> deviceNames)
		{
			ReportType = reportType;
			DeviceNames = deviceNames;
		}

		public DeviceNotFoundReportType ReportType { get; }

		public List<string> DeviceNames { get; }
	}

	/// <summary>
	/// Handles non-UI functions of connecting to an Android device via USB and working with the file system there
	/// </summary>
	class AndroidDeviceUsbConnection
	{
		public Action<Book.Book, Color, BloomPubPublishSettings> OneReadyDeviceFound;
		public Action<DeviceNotFoundReportType, List<string>> OneReadyDeviceNotFound;

		private const string kBloomFolderOnDevice = "Bloom";
		private IDevice _device;
		private string _bloomFolderPath;
		private bool _stopLookingForDevice;

		/// <summary>
		/// Attempt to establish a connection with an Android device; then send it the book.
		/// </summary>
		public void ConnectAndSendToOneDevice(Book.Book book, Color backColor, BloomPubPublishSettings settings = null)
		{
			_stopLookingForDevice = false;
			_device = null;

			//The UX here is to only allow one device plugged in a time.
			while (!_stopLookingForDevice && _device == null)
			{
				var devices = EnumerateAllDevices();
				if(!ConnectAndSendToOneDeviceInternal(devices, book, backColor, settings))
				{
					Thread.Sleep(1000);
				}
			}
		}

		public void StopFindingDevice()
		{
			_stopLookingForDevice = true;
		}

		public bool BookExists(string fileName)
		{
			if (_device == null || _bloomFolderPath == null)
				throw new InvalidOperationException("Must connect before calling BookExists");

			try
			{
				return _device.GetObjectFromPath(Path.Combine(_bloomFolderPath, fileName)) != null;
			}
			catch
			{
				// At least with our current uses of this method, it is safe to return false
				// if we can't make a successful check. This prevents nasty messages for the user. (BL-5548)
				return false;
			}
		}

		public void SendBook(string bloomdPath)
		{
			if (bloomdPath == null)
				throw new ArgumentNullException(nameof(bloomdPath));

			if (_device == null || _bloomFolderPath == null)
				throw new InvalidOperationException("Must connect before calling SendBook");

			using (var sourceStream = File.OpenRead(bloomdPath))
			using (var targetStream = _device.OpenWrite(Path.Combine(_bloomFolderPath, Path.GetFileName(bloomdPath)),
				new FileInfo(bloomdPath).Length, true))
			{
				Copy(sourceStream, targetStream);
			}

			// Also send a little marker file. BloomReader can tell "something has been updated" by just checking the modify time on this.
			var buffer = Encoding.UTF8.GetBytes("Just for change detection");
			using (var targetStream = _device.OpenWrite(Path.Combine(_bloomFolderPath, "something.modified"), buffer.Length, true))
			{
				targetStream.Write(buffer, 0, buffer.Length);
				targetStream.Flush();
			}
		}

		public string GetDeviceName()
		{
			if (_device == null)
				throw new InvalidOperationException("Must connect before calling GetDeviceName");

			return _device.Name;
		}

		private static void Copy(Stream source, Stream destination)
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
		/// Find the one Android device
		/// (indicated by the presence of the Android directory as a direct child of a root storage object)
		/// and send to it.
		/// </summary>
		/// <returns>true if it found one ready device</returns>
		private bool ConnectAndSendToOneDeviceInternal(IEnumerable<IDevice> devices, Book.Book book, Color backColor, BloomPubPublishSettings settings = null)
		{
			List<IDevice> applicableDevices = new List<IDevice>();
			foreach (var device in devices)
			{
				var androidFolderPath = GetAndroidFolderPath(device);
				if (androidFolderPath != null)
				{
					applicableDevices.Add(device);
					_bloomFolderPath = Path.GetDirectoryName(androidFolderPath) + "\\" + kBloomFolderOnDevice;
					_device = device;
				}
			}

			if (applicableDevices.Count == 1)
			{
				try
				{
					_device.CreateFolderObjectFromPath(_bloomFolderPath);
				}
				catch (Exception e)
				{
					SIL.Reporting.Logger.WriteError("Unable to create Bloom folder on device.", e);

					// Treat it as a no-device situation.
					_bloomFolderPath = null;
					_device = null;
					OneReadyDeviceNotFound?.Invoke(DeviceNotFoundReportType.NoDeviceFound, new List<string>(0));

					return false;
				}
				OneReadyDeviceFound(book, backColor, settings);
				return true;
			}

			_bloomFolderPath = null;
			_device = null;

			DeviceNotFoundReportType deviceNotFoundReportType =
				applicableDevices.Count > 1
				?
				DeviceNotFoundReportType.MoreThanOneReadyDevice
				:
				DeviceNotFoundReportType.NoDeviceFound;
			OneReadyDeviceNotFound?.Invoke(deviceNotFoundReportType,
				devices.Select(d => d.Name).ToList());

			return false;
		}

		private string GetAndroidFolderPath(IDevice device)
		{
			try
			{
				foreach (var rso in device.GetDeviceRootStorageObjects())
				{
					var possiblePath = Path.Combine(rso.Name, "Android");
					if (device.GetObjectFromPath(possiblePath) != null)
						return possiblePath;
				}
			}
			catch (COMException e)
			{
				// This can happen when the device is unplugged at just the wrong moment after we enumerated it.
				// Just treat it as an unusable device.
				SIL.Reporting.Logger.WriteError("Unable to check device for Android folder", e);
			}
			return null;
		}
	}
}
#endif
