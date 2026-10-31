using Keel.Networking.Udp.LowLevel;
using Keel.Networking.Udp.LowLevel.DataTransferLayers;
using System.Net;

namespace Keel.Networking.Tests
{
    /// <summary>
    /// Traffic from a blacklisted address is dropped anyway, so it must be dropped before the data transfer layers
    /// spend any work on it - decrypting it, counting it, or anything else a layer does.
    /// </summary>
    public class BlacklistTests : IDisposable
    {
        private const int TICK_MS = 10;

        private readonly int _receiverPort;
        private readonly UdpProtocol _receiver;
        private readonly UdpProtocol _sender = new(port: 0, protocolKey: 0);
        private readonly DataTransferAmountCaptureLayer _receivedTraffic = new();

        public BlacklistTests()
        {
            _receiverPort = Utils.GetAvailableUdpPort();
            _receiver = new UdpProtocol(_receiverPort, protocolKey: 0);
            _receiver.RegisterDataTransferLayer(_receivedTraffic);
        }

        public void Dispose()
        {
            _receiver.Dispose();
            _sender.Dispose();
        }

        [Fact]
        public void BlacklistedTrafficSkipsDataTransferLayers()
        {
            // The layer sees traffic from an address that is not blacklisted...
            Send();
            PollUntil(() => _receivedTraffic.PacketsReceived > 0, millisecondsTimeout: 1000);
            Assert.Equal(1, _receivedTraffic.PacketsReceived);

            // ...but nothing once the address is blacklisted. Loopback delivers well within the wait.
            _receiver.AddOrUpdateAddressInBlacklist(IPAddress.Loopback, millisecondsToAdd: 60_000);
            Send();
            PollUntil(() => false, millisecondsTimeout: 200);

            Assert.Equal(1, _receivedTraffic.PacketsReceived);
        }

        private void Send() =>
            _sender.SendTo(new IPEndPoint(IPAddress.Loopback, _receiverPort), new ArraySegment<byte>([42]), UdpProtocol.DeliveryMethod.Unreliable);

        private void PollUntil(Func<bool> condition, int millisecondsTimeout)
        {
            var startTime = DateTime.UtcNow;
            while (!condition() && (DateTime.UtcNow - startTime).TotalMilliseconds < millisecondsTimeout)
            {
                Thread.Sleep(TICK_MS);
                _receiver.Poll();
            }
        }
    }
}