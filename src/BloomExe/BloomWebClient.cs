using System.Net;

namespace Bloom
{
	/// <summary>
	/// We need a class that inherits from both System.Net.WebClient and this interface
	/// so that we can test what happens when Bloom is used behind a captive portal.
	/// </summary>
	public class BloomWebClient : WebClient, IBloomWebClient
	{
	}

	/// <summary>
	/// Allows moq-ing of DownloadString to return a simulated captive portal.
	/// </summary>
	public interface IBloomWebClient
	{
		// WebClient method
		string DownloadString(string url);
	}

}
