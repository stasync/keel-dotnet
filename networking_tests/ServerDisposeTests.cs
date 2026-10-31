using Keel.Networking.Udp;
using Keel.Networking.Udp.LowLevel;

namespace Keel.Networking.Tests
{
    public class ServerDisposeTests
    {
        [Fact]
        public void UpdateAfterDispose_DoesNothing()
        {
            var server = new ReliableUdpServer(maxConnections: 1, port: 0, protocolKey: 0);
            server.Dispose();

            server.Update();
        }

        [Fact]
        public void SendAfterDispose_ThrowsObjectDisposedException()
        {
            var server = new ReliableUdpServer(maxConnections: 1, port: 0, protocolKey: 0);
            server.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                server.SendToAll(new ArraySegment<byte>([1]), UdpProtocol.DeliveryMethod.Unreliable));
        }
    }
}