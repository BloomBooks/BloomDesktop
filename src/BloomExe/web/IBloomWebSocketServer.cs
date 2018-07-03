using System.ComponentModel;

namespace Bloom.web
{
	public interface IBloomWebSocketServer
	{
		void SendString(string clientContext, string eventId, string message);
		void Init(string port);
		void Dispose();
		void SendBundle(string clientContext, string progress, object messageBundle);
	}
}
