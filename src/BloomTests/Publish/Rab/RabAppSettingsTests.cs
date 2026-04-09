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
  <colors type='main'>
    <color name='PrimaryColor'>
      <color-mapping theme='Normal' value='#3F51B5'/>
    </color>
  </colors>
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
                    MainColor = "#123456",
                    PackageName = "org.sil.bloom.app",
                    IconPath = @"C:\icons\app.png",
                    Copyright = "Copyright 2026",
                }
            );
            project.Save();

            var reloadedProject = RabAppProject.Load(tempFile.Path);
            var settings = reloadedProject.GetAppSettings();

            Assert.That(reloadedProject.AppName, Is.EqualTo("Bloom App"));
            Assert.That(reloadedProject.PackageName, Is.EqualTo("org.sil.bloom.app"));
            Assert.That(settings.MainColor, Is.EqualTo("#123456"));
            Assert.That(settings.PackageName, Is.EqualTo("org.sil.bloom.app"));
            Assert.That(settings.IconPath, Is.EqualTo(@"C:\icons\app.png"));
            Assert.That(settings.Copyright, Is.EqualTo("Copyright 2026"));
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
                    MainColor = "#654321",
                    PackageName = "org.sil.bloom.updated.app",
                    IconPath = string.Empty,
                    Copyright = "Copyright 2026",
                }
            );
            project.Save();

            var reloadedProject = RabAppProject.Load(tempFile.Path);

            Assert.That(reloadedProject.PackageName, Is.EqualTo("org.sil.bloom.updated.app"));
        }
    }
}
