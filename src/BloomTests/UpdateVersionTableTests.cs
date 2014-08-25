using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    [TestFixture]
    public class UpdateVersionTableTests
    {

        [Test]
        public void ThisVersionTooLarge_ReturnsEmptyString()
        {
            var t = new UpdateVersionTable();
            t.RunningVersion = Version.Parse("99.99.99");
            t.TextContentsOfTable = @"# the format is min,max,url
                                                        0.0.0,1.1.999, http://example.com/appcast.xml";
            Assert.IsEmpty(t.GetAppcastUrl());
        }
        
        [Test]
        [ExpectedException(typeof(System.Net.WebException))]
        public void CannotReachTableOnInternet_Throws()
        {
            var t = new UpdateVersionTable();
            t.URLOfTable = "http://notthere7blah/foo.txt";
            t.GetAppcastUrl();
        }  
      
        [Test]
        public void ValueOnLowerBound_ReturnsCorrectUrl()
        {
            var t = new UpdateVersionTable(); 
            t.TextContentsOfTable = @"# the format is min,max,url
                                                        0.0.0,1.1.999, http://first.com/first
                                                        2.1.1,2.9.999, http://second.com/second
                                                        3.2.2,3.9.999, http://third.com/third";

            t.RunningVersion = Version.Parse("0.0.0");
            Assert.AreEqual("http://first.com/first", t.GetAppcastUrl());
            t.RunningVersion = Version.Parse("2.1.1");
            Assert.AreEqual("http://second.com/second", t.GetAppcastUrl());
            t.RunningVersion = Version.Parse("3.2.2");
            Assert.AreEqual("http://third.com/third", t.GetAppcastUrl());
        }

        [Test]
        public void ValueOnUpperBound_ReturnsCorrectUrl()
        {
            var t = new UpdateVersionTable(); 
            t.TextContentsOfTable = @"# the format is min,max,url
                                                        0.0.0,1.1.999, http://first.com/first
                                                        2.1.1,2.9.999, http://second.com/second
                                                        3.2.2,3.9.999, http://third.com/third";

            t.RunningVersion = Version.Parse("1.1.999");
            Assert.AreEqual("http://first.com/first", t.GetAppcastUrl());
            t.RunningVersion = Version.Parse("2.9.999");
            Assert.AreEqual("http://second.com/second", t.GetAppcastUrl());
            t.RunningVersion = Version.Parse("3.9.999");
            Assert.AreEqual("http://third.com/third", t.GetAppcastUrl());
        }

        [Test]
        public void ValueOnInMiddle_ReturnsCorrectUrl()
        {
            var t = new UpdateVersionTable(); 
            t.TextContentsOfTable = @"# the format is min,max,url
                                                        1.0.0,1.0.50, http://first.com/first
                                                        1.0.50,2.1.99, http://second.com/second
                                                        3.0.0,3.9.999, http://third.com/third";

            t.RunningVersion = Version.Parse("1.0.40");
            Assert.AreEqual("http://first.com/first", t.GetAppcastUrl());
            t.RunningVersion = Version.Parse("1.1.0");
            Assert.AreEqual("http://second.com/second", t.GetAppcastUrl());
            t.RunningVersion = Version.Parse("3.0.1");
            Assert.AreEqual("http://third.com/third", t.GetAppcastUrl());
        }

        [Test]
        public void CanReadActualFileFromServer()
        {
            var t = new UpdateVersionTable(); 
            t.RunningVersion = Version.Parse("2.1.0");
            Assert.AreEqual("http://build.palaso.org/guestAuth/repository/download/bt414/.lastSuccessful/appcast.xml", t.GetAppcastUrl());
        }
       
    }
}
