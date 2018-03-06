using System.ComponentModel;

namespace Bloom.web
{
	public interface IBloomWebSocketServer
	{
		void Send(string eventId, string eventData, string eventStyle = null);
		void Init(string port);
		void Dispose();
	}
}
