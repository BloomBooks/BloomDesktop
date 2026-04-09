using System.Xml.Linq;
using Bloom.Publish.Rab;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.Rab
{
    public class RabAppSettingsTests
    {
        private const string kSampleAppDef =
            @"<?xml version='1.0' encoding='utf-8'?>
<app-definition type='RAB' program-version='13.4'>
  <project-name>Sample Project</project-name>
  <app-name lang='default'>Sample Project</app-name>
  <package>org.sil.sample</package>
  <version code='1' name='1.0'/>
  <color-scheme name='Indigo'/>
  <colors type='main'>
    <color name='PrimaryColor'>
      <color-mapping theme='Normal' value='#3F51B5'/>
    </color>
  </colors>
  <deep-linking enabled='false'>
  </deep-linking>
  <books id='C01'>
    <book-collection-name>Main Collection</book-collection-name>
    <metadata>
      <meta name='copyright-text' content='copyright'/>
    </metadata>
  </books>
</app-definition>";

        [Test]
        public void SetAppSettings_RoundTripsAppMetadata()
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(tempFile.Path, kSampleAppDef);

            var project = RabAppProject.Load(tempFile.Path);
            project.SetAppSettings(
                new RabAppSettings()
                {
                    AppName = "Bloom App",
                    ColorScheme = "Lime",
                    PackageName = "org.sil.bloom.app",
                    IconPath = @"C:\icons\app.png",
                    Copyright = "Copyright 2026",
                    About = "About this app",
                }
            );
            project.Save();

            var reloadedProject = RabAppProject.Load(tempFile.Path);
            var settings = reloadedProject.GetAppSettings();
            var appDef = XDocument.Load(tempFile.Path);

            Assert.That(reloadedProject.AppName, Is.EqualTo("Bloom App"));
            Assert.That(reloadedProject.PackageName, Is.EqualTo("org.sil.bloom.app"));
            Assert.That(settings.ColorScheme, Is.EqualTo("Lime"));
            Assert.That(settings.PackageName, Is.EqualTo("org.sil.bloom.app"));
            Assert.That(settings.IconPath, Is.EqualTo(@"C:\icons\app.png"));
            Assert.That(settings.Copyright, Is.EqualTo("Copyright 2026"));
            Assert.That(
                appDef
                    .Root?.Element("colors")
                    ?.Element("color")
                    ?.Element("color-mapping")
                    ?.Attribute("value")
                    ?.Value,
                Is.EqualTo(RabAppProject.DefaultPrimaryColor)
            );
            Assert.That(
                appDef.Root?.Element("about")?.Attribute("enabled")?.Value,
                Is.EqualTo("true")
            );
            Assert.That(
                appDef.Root?.Element("about")?.Element("filename")?.Value,
                Is.EqualTo(RabAppProject.DefaultAboutFileName)
            );
        }

        [Test]
        public void SetAppSettings_UpdatesPackageElement()
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(tempFile.Path, kSampleAppDef);

            var project = RabAppProject.Load(tempFile.Path);
            project.SetAppSettings(
                new RabAppSettings()
                {
                    AppName = "Bloom App",
                    ColorScheme = "Dark Red",
                    PackageName = "org.sil.bloom.updated.app",
                    IconPath = string.Empty,
                    Copyright = "Copyright 2026",
                    About = "About this app",
                }
            );
            project.Save();

            var reloadedProject = RabAppProject.Load(tempFile.Path);

            Assert.That(reloadedProject.PackageName, Is.EqualTo("org.sil.bloom.updated.app"));
            Assert.That(reloadedProject.GetAppSettings().ColorScheme, Is.EqualTo("Dark Red"));
        }

        [Test]
        public void GetAppSettings_PrefersAdaptiveForegroundIconOverLauncherIcons()
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(
                tempFile.Path,
                @"<?xml version='1.0' encoding='utf-8'?>
<app-definition type='RAB' program-version='13.4'>
  <project-name>Sample Project</project-name>
  <app-name lang='default'>Sample Project</app-name>
  <package>org.sil.sample</package>
  <version code='1' name='1.0'/>
  <images type='launcher'>
    <image width='512' height='512'>drawable-web\ic_launcher.png</image>
  </images>
  <adaptive-icon>
    <foreground>
      <image>ic_launcher_foreground.png</image>
    </foreground>
  </adaptive-icon>
  <books id='C01'>
    <book-collection-name>Main Collection</book-collection-name>
  </books>
</app-definition>"
            );

            var projectDataPath = Path.Combine(
                Path.GetDirectoryName(tempFile.Path) ?? string.Empty,
                Path.GetFileNameWithoutExtension(tempFile.Path) + "_data"
            );
            var launcherIconPath = Path.Combine(
                projectDataPath,
                "images",
                "drawable-web",
                "ic_launcher.png"
            );
            var adaptiveIconPath = Path.Combine(
                projectDataPath,
                "images",
                "mipmap-xxxhdpi",
                "ic_launcher_foreground.png"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(launcherIconPath));
            Directory.CreateDirectory(Path.GetDirectoryName(adaptiveIconPath));
            RobustFile.WriteAllText(launcherIconPath, "launcher");
            RobustFile.WriteAllText(adaptiveIconPath, "adaptive");

            var project = RabAppProject.Load(tempFile.Path);

            Assert.That(project.GetAppSettings().IconPath, Is.EqualTo(adaptiveIconPath));
        }
    }
}
