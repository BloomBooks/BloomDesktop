using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// The methods in this class run once before and after each test run, i.e. they get
    /// executed exactly once.
    /// </summary>
    [SetUpFixture]
    public class SetupFixture
    {
        [OneTimeSetUp]
        public void Setup()
        {
            L10NSharp.LocalizationManager.StrictInitializationMode = false;
            MakeWpfNativeCodeLoadable();
        }

        /// <summary>
        /// Lets WPF's imaging code find its own native library when we are run by the NUnit
        /// console runner, which is how the build does it (see the NUnit3 task in Bloom.proj).
        ///
        /// The production code that needs this is Bloom.web.controllers.ImageGalleryApi, which
        /// is the one place in Bloom that uses WPF's WIC-backed imaging
        /// (System.Windows.Media.Imaging: BitmapDecoder to read an image's header, BitmapImage
        /// and JpegBitmapEncoder to build the downscaled preview the image chooser shows for a
        /// file the browser cannot display). Its tests — ImageGalleryApiTests — are therefore
        /// the only ones this affects today, and if that usage ever goes away, so can this.
        ///
        /// That runner launches a per-framework agent process whose runtimeconfig.json asks
        /// only for Microsoft.NETCore.App, never Microsoft.WindowsDesktop.App. The managed WPF
        /// assemblies still load — NUnit resolves those through our deps.json — but the
        /// runtime's native search path is built from what the *process* asked for, so it holds
        /// the NETCore.App folder alone. The first WPF imaging call then dies with
        /// "Unable to load DLL 'wpfgfx_cor3.dll'", which is next door in the WindowsDesktop
        /// folder that nobody put on the path. Both agent versions we have shipped (3.20.1 and
        /// 3.22.0) are built this way, so this is not something a runner upgrade fixes.
        ///
        /// Nothing is wrong with Bloom itself: Bloom.exe sets UseWPF and so is a genuine
        /// WindowsDesktop app, whose native path includes that folder. Nor is anything wrong
        /// under `dotnet test`, which starts the host from BloomTests.runtimeconfig.json — and
        /// that one does ask for WindowsDesktop. So a test that needs WPF passes locally and
        /// fails on the build machine, which is how those tests came to be merged green and
        /// then turn TeamCity red (BL-16597).
        ///
        /// PresentationCore is loaded out of the very folder its native library lives in, so
        /// that folder is simply where the assembly already is. Returning zero for anything not
        /// found there leaves normal resolution — and its normal error — untouched.
        /// </summary>
        private static void MakeWpfNativeCodeLoadable()
        {
            var presentationCore = typeof(BitmapDecoder).Assembly;
            var windowsDesktopFolder = Path.GetDirectoryName(presentationCore.Location);
            NativeLibrary.SetDllImportResolver(
                presentationCore,
                (libraryName, assembly, searchPath) =>
                    NativeLibrary.TryLoad(
                        Path.Combine(windowsDesktopFolder, libraryName),
                        out var library
                    )
                        ? library
                        : IntPtr.Zero
            );
        }
    }
}
