using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SIL.Reporting;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Bloom.web.controllers
{
    internal static class WindowsGraphicsCaptureScreenshot
    {
        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            [PreserveSig]
            int CreateForWindow(IntPtr window, [In] ref Guid iid, out IntPtr result);

            [PreserveSig]
            int CreateForMonitor(IntPtr monitor, [In] ref Guid iid, out IntPtr result);
        }

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            D3DDriverType driverType,
            IntPtr software,
            uint flags,
            IntPtr pFeatureLevels,
            uint featureLevels,
            uint sdkVersion,
            out IntPtr ppDevice,
            out uint pFeatureLevel,
            out IntPtr ppImmediateContext
        );

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice
        );

        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(
            string sourceString,
            int length,
            out IntPtr hstring
        );

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(
            IntPtr activatableClassId,
            [In] ref Guid iid,
            out IntPtr factory
        );

        private enum D3DDriverType : uint
        {
            Hardware = 1,
            Warp = 5,
        }

        private const uint D3D11CreateDeviceBgraSupport = 0x20;
        private const uint D3D11SdkVersion = 7;
        private const string GraphicsCaptureItemRuntimeClass =
            "Windows.Graphics.Capture.GraphicsCaptureItem";
        private static readonly Guid ActivationFactoryGuid = new Guid(
            "00000035-0000-0000-C000-000000000046"
        );
        private static readonly Guid GraphicsCaptureItemGuid = new Guid(
            "79C3F95B-31F7-4EC2-A464-632EF5D30760"
        );
        private static readonly Guid GraphicsCaptureItemInteropGuid = new Guid(
            "3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"
        );

        private static readonly Guid IidIdxgiDevice = new Guid(
            "54EC77FA-1377-44E6-8C32-88FD5F44C84C"
        );

        public static bool TryCapture(Control controlForScreenshotting, out Bitmap screenshot)
        {
            screenshot = null;

            if (
                controlForScreenshotting == null
                || controlForScreenshotting.IsDisposed
                || !GraphicsCaptureSession.IsSupported()
            )
                return false;

            var form = controlForScreenshotting.FindForm();
            if (form?.Handle == IntPtr.Zero)
                return false;

            if (!TryCreateCaptureItemForWindow(form.Handle, out var item))
                return false;

            var targetRect = GetTargetRectInWindowPixels(controlForScreenshotting, form);
            if (targetRect.Width <= 0 || targetRect.Height <= 0)
                return false;

            var d3dDevice = CreateD3DDevice();
            if (d3dDevice == null)
                return false;

            using var frameReady = new AutoResetEvent(false);
            Direct3D11CaptureFrame frame = null;
            void FrameArrivedHandler(Direct3D11CaptureFramePool pool, object _)
            {
                try
                {
                    frame?.Dispose();
                    frame = pool.TryGetNextFrame();
                }
                catch (Exception)
                {
                    // Ignore and let timeout/failure path handle it.
                }
                finally
                {
                    frameReady.Set();
                }
            }

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                d3dDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size
            );
            using var session = framePool.CreateCaptureSession(item);

            framePool.FrameArrived += FrameArrivedHandler;

            try
            {
                session.StartCapture();

                if (!frameReady.WaitOne(1200))
                    return false;

                if (frame == null || frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0)
                    return false;

                if (!TryConvertFrameToBitmap(frame, out var windowBitmap))
                    return false;

                using (windowBitmap)
                {
                    var scaledCrop = ScaleCropRect(
                        targetRect,
                        item.Size,
                        new System.Drawing.Size(frame.ContentSize.Width, frame.ContentSize.Height)
                    );
                    if (scaledCrop.Width <= 0 || scaledCrop.Height <= 0)
                        return false;

                    screenshot = windowBitmap.Clone(scaledCrop, PixelFormat.Format32bppArgb);
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.WriteError("Windows.Graphics.Capture screenshot failed.", e);
                return false;
            }
            finally
            {
                framePool.FrameArrived -= FrameArrivedHandler;
                frame?.Dispose();
            }
        }

        private static bool TryCreateCaptureItemForWindow(IntPtr hwnd, out GraphicsCaptureItem item)
        {
            item = null;

            try
            {
                if (
                    WindowsCreateString(
                        GraphicsCaptureItemRuntimeClass,
                        GraphicsCaptureItemRuntimeClass.Length,
                        out var classId
                    ) < 0
                )
                    return false;

                IntPtr activationFactoryPtr = IntPtr.Zero;
                try
                {
                    var factoryGuid = ActivationFactoryGuid;
                    var roGetHr = RoGetActivationFactory(
                        classId,
                        ref factoryGuid,
                        out activationFactoryPtr
                    );
                    if (roGetHr < 0 || activationFactoryPtr == IntPtr.Zero)
                        return false;

                    IntPtr interopPtr = IntPtr.Zero;
                    try
                    {
                        var interopGuid = GraphicsCaptureItemInteropGuid;
                        var qiHr = Marshal.QueryInterface(
                            activationFactoryPtr,
                            ref interopGuid,
                            out interopPtr
                        );
                        if (qiHr < 0 || interopPtr == IntPtr.Zero)
                            return false;

                        var interop = (IGraphicsCaptureItemInterop)
                            Marshal.GetObjectForIUnknown(interopPtr);
                        var itemPtr = IntPtr.Zero;
                        var itemGuid = GraphicsCaptureItemGuid;
                        var createHr = interop.CreateForWindow(hwnd, ref itemGuid, out itemPtr);
                        if (createHr < 0 || itemPtr == IntPtr.Zero)
                            return false;

                        try
                        {
                            item = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
                        }
                        finally
                        {
                            Marshal.Release(itemPtr);
                        }
                    }
                    finally
                    {
                        if (interopPtr != IntPtr.Zero)
                            Marshal.Release(interopPtr);
                    }

                    return item != null;
                }
                finally
                {
                    if (activationFactoryPtr != IntPtr.Zero)
                        Marshal.Release(activationFactoryPtr);
                    WindowsDeleteString(classId);
                }
            }
            catch (Exception e)
            {
                Logger.WriteError("Failed to create GraphicsCaptureItem for window.", e);
                return false;
            }
        }

        private static IDirect3DDevice CreateD3DDevice()
        {
            IntPtr d3dDevicePtr = IntPtr.Zero;
            IntPtr d3dContextPtr = IntPtr.Zero;
            IntPtr dxgiDevicePtr = IntPtr.Zero;
            IntPtr inspectableDevicePtr = IntPtr.Zero;

            try
            {
                uint featureLevel;
                var hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    D3DDriverType.Hardware,
                    IntPtr.Zero,
                    D3D11CreateDeviceBgraSupport,
                    IntPtr.Zero,
                    0,
                    D3D11SdkVersion,
                    out d3dDevicePtr,
                    out featureLevel,
                    out d3dContextPtr
                );

                if (hr < 0)
                {
                    hr = D3D11CreateDevice(
                        IntPtr.Zero,
                        D3DDriverType.Warp,
                        IntPtr.Zero,
                        D3D11CreateDeviceBgraSupport,
                        IntPtr.Zero,
                        0,
                        D3D11SdkVersion,
                        out d3dDevicePtr,
                        out featureLevel,
                        out d3dContextPtr
                    );
                }

                if (hr < 0 || d3dDevicePtr == IntPtr.Zero)
                    return null;

                var dxgiGuid = IidIdxgiDevice;
                hr = Marshal.QueryInterface(d3dDevicePtr, ref dxgiGuid, out dxgiDevicePtr);
                if (hr < 0 || dxgiDevicePtr == IntPtr.Zero)
                    return null;

                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out inspectableDevicePtr);
                if (hr < 0 || inspectableDevicePtr == IntPtr.Zero)
                    return null;

                return (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectableDevicePtr);
            }
            catch (Exception e)
            {
                Logger.WriteError("Failed to create Direct3D device for capture.", e);
                return null;
            }
            finally
            {
                if (inspectableDevicePtr != IntPtr.Zero)
                    Marshal.Release(inspectableDevicePtr);
                if (dxgiDevicePtr != IntPtr.Zero)
                    Marshal.Release(dxgiDevicePtr);
                if (d3dContextPtr != IntPtr.Zero)
                    Marshal.Release(d3dContextPtr);
                if (d3dDevicePtr != IntPtr.Zero)
                    Marshal.Release(d3dDevicePtr);
            }
        }

        private static Rectangle GetTargetRectInWindowPixels(Control control, Form form)
        {
            var controlBounds =
                WindowsMonitorScaling.GetRectangleFromControlScaledToMonitorResolution(control);
            var windowBounds =
                WindowsMonitorScaling.GetRectangleFromControlScaledToMonitorResolution(form);

            var x = controlBounds.Left - windowBounds.Left;
            var y = controlBounds.Top - windowBounds.Top;
            return new Rectangle(x, y, controlBounds.Width, controlBounds.Height);
        }

        private static Rectangle ScaleCropRect(
            Rectangle sourceRect,
            Windows.Graphics.SizeInt32 sourceWindowSize,
            System.Drawing.Size frameSize
        )
        {
            if (
                sourceWindowSize.Width <= 0
                || sourceWindowSize.Height <= 0
                || frameSize.Width <= 0
                || frameSize.Height <= 0
            )
                return Rectangle.Empty;

            var scaleX = frameSize.Width / (double)sourceWindowSize.Width;
            var scaleY = frameSize.Height / (double)sourceWindowSize.Height;

            var x = (int)Math.Floor(sourceRect.Left * scaleX);
            var y = (int)Math.Floor(sourceRect.Top * scaleY);
            var right = (int)Math.Ceiling(sourceRect.Right * scaleX);
            var bottom = (int)Math.Ceiling(sourceRect.Bottom * scaleY);

            x = Math.Max(0, x);
            y = Math.Max(0, y);
            right = Math.Min(frameSize.Width, right);
            bottom = Math.Min(frameSize.Height, bottom);

            return right > x && bottom > y
                ? Rectangle.FromLTRB(x, y, right, bottom)
                : Rectangle.Empty;
        }

        private static bool TryConvertFrameToBitmap(Direct3D11CaptureFrame frame, out Bitmap bitmap)
        {
            bitmap = null;

            SoftwareBitmap softwareBitmap = null;
            SoftwareBitmap convertedBitmap = null;

            try
            {
                softwareBitmap = SoftwareBitmap
                    .CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                convertedBitmap =
                    softwareBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                    && softwareBitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied
                        ? softwareBitmap
                        : SoftwareBitmap.Convert(
                            softwareBitmap,
                            BitmapPixelFormat.Bgra8,
                            BitmapAlphaMode.Premultiplied
                        );

                var width = convertedBitmap.PixelWidth;
                var height = convertedBitmap.PixelHeight;
                if (width <= 0 || height <= 0)
                    return false;

                var bytes = new byte[checked(width * height * 4)];
                var buffer = new Windows.Storage.Streams.Buffer((uint)bytes.Length);
                convertedBitmap.CopyToBuffer(buffer);
                var dataReader = DataReader.FromBuffer(buffer);
                dataReader.ReadBytes(bytes);

                bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, width, height);
                var bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);

                try
                {
                    var sourceStride = width * 4;
                    if (bitmapData.Stride == sourceStride)
                    {
                        Marshal.Copy(bytes, 0, bitmapData.Scan0, bytes.Length);
                    }
                    else
                    {
                        for (var y = 0; y < height; y++)
                        {
                            var sourceOffset = y * sourceStride;
                            var destinationOffset = IntPtr.Add(
                                bitmapData.Scan0,
                                y * bitmapData.Stride
                            );
                            Marshal.Copy(bytes, sourceOffset, destinationOffset, sourceStride);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }

                return true;
            }
            catch (Exception e)
            {
                bitmap?.Dispose();
                bitmap = null;
                Logger.WriteError("Failed to convert capture frame to bitmap.", e);
                return false;
            }
            finally
            {
                if (convertedBitmap != null && !ReferenceEquals(convertedBitmap, softwareBitmap))
                    convertedBitmap.Dispose();
                softwareBitmap?.Dispose();
            }
        }
    }
}
