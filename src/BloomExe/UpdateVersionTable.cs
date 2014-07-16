using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Amazon.DataPipeline.Model;
using Amazon.ElasticMapReduce.Model;
using Bloom.ImageProcessing;

namespace Bloom
{
    /// <summary>
    /// 
    /// This could maybe eventually go to https://github.com/hatton/NetSparkle. My hesitation is that it's kind of specific to our way of using TeamCity and our build scripts
    /// 
    /// We have a way to learn of updates by downloading an appcast.xml from a url. But for different versions of the app, we may want to recommend different
    /// version. So with this class we download a simple table on from a fixed ftp site, read it, and determine URL we should be getting
    /// the appcast.xml from (each comes from a different Team City configuration).
    /// </summary>
    public class UpdateVersionTable
    {
        //unit tests can change this
        public string URLOfTable = "http://bloomlibrary.org/channels/VersionUpgradeTable.txt";
        //unit tests can pre-set this
        public  string TextContentsOfTable { get; set; }
        
        //unit tests can pre-set this
        public  Version RunningVersion { get; set; }


        /// <summary>
        /// Note! This will propogate network exceptions, so client can catch them and warn or not warn the user.
        /// </summary>
        /// <returns></returns>
        public  string GetAppcastUrl()
        {
            if (string.IsNullOrEmpty(TextContentsOfTable))
            {
                var client = new WebClient();
                {
                    TextContentsOfTable =  client.DownloadString(URLOfTable);
                }
            }
            if (RunningVersion == default(Version))
            {
                RunningVersion = Assembly.GetExecutingAssembly().GetName().Version;
            }

            //NB Programmers: don't change this to some OS-specific line ending, this is  file read by both OS's. '\n' is common to files edited on linux and windows.
            foreach (var line in TextContentsOfTable.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.TrimStart().StartsWith("#"))
                    continue; //comment

                var parts = line.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
                if(parts.Length!=3)
                    throw new ApplicationException("Could not parse a line of the UpdateVersionTable on "+URLOfTable+" '"+line+"'");
                var lower = Version.Parse(parts[0]);
                var upper = Version.Parse(parts[1]);
                if (lower <= RunningVersion && upper >= RunningVersion)
                    return parts[2].Trim();
            }
            return string.Empty;
        }
    }
}
