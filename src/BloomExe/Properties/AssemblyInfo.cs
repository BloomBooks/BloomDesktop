using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Bloom")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("SIL")]
[assembly: AssemblyProduct("Bloom")]
[assembly: AssemblyCopyright("© SIL International 2012-2020")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("0afa7c29-4107-47a8-88cc-c15cb769f35e")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// Note that an automated process updates these in the TeamCity build; these ones however are important
// for whether a local build satisfies BloomParseClient.GetIsThisVersionAllowedToUpload.
// [assembly: AssemblyVersion("0.9.999.0")]
[assembly: AssemblyVersion("5.6.000.0")]
[assembly: AssemblyFileVersion("5.6.000.0")]
[assembly: AssemblyInformationalVersion("5.6.000.0")]
[assembly: InternalsVisibleTo("BloomTests")]
[assembly: InternalsVisibleTo("BloomHarvester")]
[assembly: InternalsVisibleTo("BloomHarvesterTests")]
[assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]

// Without explicitly disabling DPI awareness here, the subsequent
// loading of some System.Windows.Media components will cause the
// application to change from DPI "Unaware" to DPI "System Aware".
// The one place we know this happens currently is when loading font metadata.
// This causes problems when one monitor is set to different font scaling than another.
// Depending on how far the UI has gotten in setting up when the awareness status changes,
// it will either result in inconsistent font/icon sizing or the whole
// window will shrink down. See BL-10981.
[assembly: System.Windows.Media.DisableDpiAwareness]
