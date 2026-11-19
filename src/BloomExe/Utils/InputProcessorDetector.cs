using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bloom.Utils
{
    /// <summary>
    /// Tells whether the input method active on the calling thread is a Text Services Framework
    /// (TSF) "input processor", such as Keyman or a Windows IME, rather than a plain keyboard
    /// layout. The editor turns its long-press character popup off when one is, because holding
    /// a key down fights with the input method (BL-1071).
    /// </summary>
    /// <remarks>
    /// Ported from libpalaso 18.0.0: KeyboardController.IsFormUsingInputProcessor and the TSF
    /// declarations in Windows/MsTsfInterfaces.cs, both in SIL.Windows.Forms.Keyboarding. That
    /// call was all Bloom used from the package, and the package costs more than it gives us: it
    /// targets WinForms only, and it brings in ibusdotnet and KeymanLegacyBundle, which ship only
    /// .NET Framework builds. No other SIL project calls the method (FieldWorks, WeSay and others
    /// use the package for keyboard switching, which Bloom doesn't do), so we copied the check here
    /// rather than splitting it out of libpalaso.
    /// TSF keeps the active profile per thread, so call this on the UI thread, where the user types.
    /// </remarks>
    public static class InputProcessorDetector
    {
        /// <summary>
        /// True if the calling thread's active keyboard input method is a TSF input processor.
        /// </summary>
        public static bool IsInputProcessorActive()
        {
            if (!SIL.PlatformUtilities.Platform.IsWindows)
                return false;
            var profileMgr = CreateProfileManager();
            if (profileMgr == null)
                return false;
            var category = TfcatTipKeyboard;
            var profile = profileMgr.GetActiveProfile(ref category);
            return profile.ProfileType == TfProfileType.InputProcessor;
        }

        /// <summary>
        /// The TSF profile manager, or null if TSF does not provide one (as libpalaso allowed for).
        /// </summary>
        internal static ITfInputProcessorProfileMgr CreateProfileManager()
        {
            object profiles;
            try
            {
                profiles = Activator.CreateInstance(
                    Type.GetTypeFromCLSID(ClsidTfInputProcessorProfiles, true)
                );
            }
            catch (InvalidCastException)
            {
                return null;
            }
            return profiles as ITfInputProcessorProfileMgr;
        }

        // CLSID_TF_InputProcessorProfiles
        private static readonly Guid ClsidTfInputProcessorProfiles = new Guid(
            "33c53a50-f456-4884-b049-85fd643ecfed"
        );

        // GUID_TFCAT_TIP_KEYBOARD
        internal static readonly Guid TfcatTipKeyboard = new Guid(
            "34745c63-b2f0-4784-8b67-5e12c8701a31"
        );
    }

    // Pack 0 so that it works for both 64 and 32bit (as in libpalaso)
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    internal struct TfInputProcessorProfile
    {
        public TfProfileType ProfileType;
        public ushort LangId;
        public Guid ClsId;
        public Guid GuidProfile;
        public Guid CatId;
        public IntPtr HklSubstitute;
        public uint Caps;
        public IntPtr Hkl;
        public uint Flags;
    }

    internal enum TfProfileType : uint
    {
        Illegal = 0,
        InputProcessor = 1,
        KeyboardLayout = 2,
    }

    internal enum TfIppMf : uint
    {
        ForProcess = 0x10000000,
    }

    /// <summary>
    /// ITfInputProcessorProfileMgr. COM finds a method by its position, so every slot is
    /// declared in order. Bloom calls only GetActiveProfile; ActivateProfile and EnumProfiles
    /// are used by the unit tests. The Unused slots stand in for methods nobody calls, so that
    /// no one calls them with a guessed signature.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("71c6e74c-0f28-11d8-a82a-00065b84435c")]
    internal interface ITfInputProcessorProfileMgr
    {
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void ActivateProfile(
            TfProfileType dwProfileType,
            ushort langid,
            ref Guid clsid,
            ref Guid guidProfile,
            IntPtr hkl,
            TfIppMf dwFlags
        );

        void UnusedDeactivateProfile();

        void UnusedGetProfile();

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        [return: MarshalAs(UnmanagedType.Interface)]
        IEnumTfInputProcessorProfiles EnumProfiles(short langid);

        void UnusedReleaseInputProcessor();

        void UnusedRegisterProfile();

        void UnusedUnregisterProfile();

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        [return: MarshalAs(UnmanagedType.Struct)]
        TfInputProcessorProfile GetActiveProfile(ref Guid catid);
    }

    /// <summary>
    /// IEnumTfInputProcessorProfiles, used by the unit tests to find an installed input processor.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("71c6e74d-0f28-11d8-a82a-00065b84435c")]
    internal interface IEnumTfInputProcessorProfiles
    {
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        [return: MarshalAs(UnmanagedType.Interface)]
        IEnumTfInputProcessorProfiles Clone();

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        int Next(
            uint ulCount,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]
                TfInputProcessorProfile[] profiles
        );

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void Reset();

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void Skip(uint ulCount);
    }
}
