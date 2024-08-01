using Bloom.MiscUI;
using NUnit.Framework;
#if !__MonoCS__
using SIL.Media.Naudio;
#endif

namespace BloomTests
{
    [TestFixture]
    public class MiscellaneousTests
    {
#if !__MonoCS__
        /// <summary>
        /// BL-2974. We don't directly use the reference to NAudio in Bloom.exe, it's just there to make sure the DLL gets copied.
        /// But the build won't actually fail if it's not there to be copied. Creating this object will fail if it isn't.
        /// The prevents us from shipping a build if we somehow mess up the TeamCity configuration so the dependencies don't
        /// load a useable NAudio.dll.
        /// </summary>
        [Test]
        public void NAudioIsInstalled()
        {
            Assert.DoesNotThrow(() => new AudioRecorder(1));
        }
#endif

        [Test]
        public void DoubleCheckFileFilterWorks()
        {
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(null, "foo.txt"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(null, "foo.doc"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(null, "foo"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(null, "foo."));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(null, "foo.bar"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(null, "foo.bar.baz"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter("", "foo.txt"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter("", "foo.doc"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter("", "foo"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter("", "foo."));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter("", "foo.bar"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter("", "foo.bar.baz"));

            var filterAll = "All files|*.*";
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterAll, "foo.txt"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterAll, "foo.doc"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterAll, "foo"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterAll, "foo."));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterAll, "foo.bar"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterAll, "foo.bar.baz"));

            var filterTxtOnly = "Text files|*.txt";
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtOnly, "foo.txt"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtOnly, "foo.doc"));

            var filterTxtAndDoc = "Text files|*.txt|Word files|*.doc";
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDoc, "foo.txt"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDoc, "foo.doc"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDoc, "foo"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDoc, "foo."));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDoc, "foo.bar"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDoc, "foo.txt.baz"));

            var filterTxtAndDocWithAll = "Text files|*.txt|Word files|*.doc|All files|*.*";
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDocWithAll, "foo.txt"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDocWithAll, "foo.doc"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDocWithAll, "foo"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDocWithAll, "foo."));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDocWithAll, "foo.bar"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterTxtAndDocWithAll, "foo.txt.baz"));

            var filterCodeSourceFiles = "Code source files|*.cs;*.cpp;*.js;*.tsx";
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCodeSourceFiles, "foo.cs"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCodeSourceFiles, "foo.cpp"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCodeSourceFiles, "foo.js"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCodeSourceFiles, "foo.tsx"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterCodeSourceFiles, "foo"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterCodeSourceFiles, "foo."));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterCodeSourceFiles, "foo.bar"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterCodeSourceFiles, "foo.cs.baz"));

            var filterCsCppSourceFiles = "C# source files|*.cs;*.csproj|C++ source files|*.cpp;*.cc;*.h;*.vcproj";
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo.cs"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo.csproj"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo.cpp"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo.cc"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo.h"));
            Assert.IsTrue(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo.vcproj"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo."));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo.bar"));
            Assert.IsFalse(BloomOpenFileDialog.DoubleCheckFileFilter(filterCsCppSourceFiles, "foo.cs.baz"));
        }
    }
}
