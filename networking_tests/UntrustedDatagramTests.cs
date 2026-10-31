using Keel.Networking.Tests.DataTransferLayers;
using Keel.Networking.Udp;
using Keel.Networking.Udp.LowLevel;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Keel.Networking.Tests
{
    /// <summary>
    /// Every field of an incoming datagram is under the sender's control, so none of them may be used as an index
    /// before it's checked. A single datagram must never throw out of Poll() or Update(): when they run inside a
    /// worker, that would end the update loop for good.
    ///
    /// The datagrams are written by hand and sent through a raw socket, since <see cref="UdpProtocol"/> only ever
    /// sends well-formed ones. Header layout: protocol key (2 bytes), datagram type (1), channel (1), datagram uid (4).
    /// </summary>
    public class UntrustedDatagramTests : IDisposable
    {
        private const int HEADER_SIZE = 8;
        private const byte PAYLOAD = 0;
        private const byte ACK = 1;
        private const int TICK_MS = 10;

        private readonly int _protocolPort;
        private readonly UdpProtocol _protocol;
        private readonly UdpClient _rawSocket = new(new IPEndPoint(IPAddress.Loopback, 0));

        private int _protocolFailureCount;

        public UntrustedDatagramTests()
        {
            _protocolPort = Utils.GetAvailableUdpPort();
            _protocol = new UdpProtocol(_protocolPort, protocolKey: 0);
            _protocol.FailedToProcessDatagram += _ => _protocolFailureCount++;
        }

        public void Dispose()
        {
            _protocol.Dispose();
            _rawSocket.Dispose();
        }

        [Fact]
        public void PayloadOnChannelWithoutDeliveryMethodIsRejected()
        {
            // The transport has a channel 5, but no delivery method maps to it.
            SendRaw(_protocolPort, Datagram(PAYLOAD, channel: 5, datagramUid: 1, 42));

            PollUntil(_protocol.Poll, () => _protocolFailureCount > 0);

            Assert.Equal(1, _protocolFailureCount);
            Assert.False(_protocol.TryDequeueIncoming(out _));
        }

        [Fact]
        public void PayloadOnChannelUnknownToTransportIsRejected()
        {
            SendRaw(_protocolPort, Datagram(PAYLOAD, channel: 200, datagramUid: 1, 42));

            PollUntil(_protocol.Poll, () => _protocolFailureCount > 0);

            Assert.Equal(1, _protocolFailureCount);
            Assert.False(_protocol.TryDequeueIncoming(out _));
        }

        [Fact]
        public void AckWithoutAcknowledgedChannelIsRejected()
        {
            // Header only - the channel of the acknowledged datagram should follow it.
            SendRaw(_protocolPort, Datagram(ACK, channel: 0, datagramUid: 1));

            PollUntil(_protocol.Poll, () => _protocolFailureCount > 0);

            Assert.Equal(1, _protocolFailureCount);
        }

        [Fact]
        public void AckForChannelUnknownToTransportIsRejected()
        {
            // Acks are only looked up for endpoints that were sent something, so the raw socket gets a datagram first.
            SendReliableToRawSocket();
            SendRaw(_protocolPort, Datagram(ACK, channel: 0, datagramUid: 1, 200));

            PollUntil(_protocol.Poll, () => _protocolFailureCount > 0);

            Assert.Equal(1, _protocolFailureCount);
        }

        [Fact]
        public void AckForChannelNeverSentOnIsIgnored()
        {
            // Only the Reliable channel was sent on, so there is no state to release on ReliableOrdered.
            SendReliableToRawSocket();
            SendRaw(_protocolPort, Datagram(ACK, channel: 0, datagramUid: 1, (byte)UdpProtocol.DeliveryMethod.ReliableOrdered));

            // Nothing to wait for - the ack is legal, it just has no effect. Loopback delivers well within this.
            PollUntil(_protocol.Poll, () => false, millisecondsTimeout: 200);

            Assert.Equal(0, _protocolFailureCount);
        }

        [Fact]
        public void DatagramEmptiedByDataTransferLayerIsRejected()
        {
            var prefixLayer = new PrefixDataTransferLayer();
            _protocol.RegisterDataTransferLayer(prefixLayer);

            // What the layer sends for an empty message: the prefix alone, which it strips again on the way in.
            var datagram = new ArraySegment<byte>([]);
            prefixLayer.ProcessOutgoingData(targetEndPoint: null, ref datagram);
            SendRaw(_protocolPort, datagram.ToArray());

            PollUntil(_protocol.Poll, () => _protocolFailureCount > 0);

            Assert.Equal(1, _protocolFailureCount);
        }

        [Fact]
        public void ServerSurvivesPayloadOnUnknownChannel()
        {
            using var server = new ReliableUdpServer(maxConnections: 1, port: 0, protocolKey: 0);
            var unexpectedActionCount = 0;
            server.UnexpectedClientAction += (_, _) => unexpectedActionCount++;

            SendRaw(server.Port, Datagram(PAYLOAD, channel: 5, datagramUid: 1, 42));

            PollUntil(server.Update, () => unexpectedActionCount > 0);

            Assert.Equal(1, unexpectedActionCount);
        }

        [Fact]
        public void ServerRejectsSlotOutsideItsRange()
        {
            using var server = new ReliableUdpServer(maxConnections: 4, port: 0, protocolKey: 0);
            var unexpectedActions = new List<string>();
            server.UnexpectedClientAction += (_, message) => unexpectedActions.Add(message);

            // A well-formed heartbeat, apart from the slot - the server only has 4 of them.
            var writer = new NetWriter();
            writer.WriteByte((byte)ReliableUdpServer.ClientMessageCodes.Heartbeat);
            writer.WritePackedUInt32(1);
            writer.WritePackedUInt32(1);
            writer.WriteByte(200);
            SendRaw(server.Port, Datagram(PAYLOAD, channel: 0, datagramUid: 1, writer.ToArray()));

            PollUntil(server.Update, () => unexpectedActions.Count > 0);

            Assert.Single(unexpectedActions);
            Assert.Contains("slot '200'", unexpectedActions[0]);
        }

        private void SendRaw(int port, byte[] datagram) =>
            _rawSocket.Send(datagram, datagram.Length, new IPEndPoint(IPAddress.Loopback, port));

        private void SendReliableToRawSocket() =>
            _protocol.SendTo((IPEndPoint)_rawSocket.Client.LocalEndPoint, new ArraySegment<byte>([1]), UdpProtocol.DeliveryMethod.Reliable);

        private static byte[] Datagram(byte datagramType, byte channel, uint datagramUid, params byte[] body)
        {
            // Protocol key 0 - the first two bytes stay zero.
            var datagram = new byte[HEADER_SIZE + body.Length];
            datagram[2] = datagramType;
            datagram[3] = channel;
            BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(start: 4), datagramUid);
            body.CopyTo(datagram, HEADER_SIZE);
            return datagram;
        }

        private static void PollUntil(Action poll, Func<bool> condition, int millisecondsTimeout = 1000)
        {
            var startTime = DateTime.UtcNow;
            while (!condition() && (DateTime.UtcNow - startTime).TotalMilliseconds < millisecondsTimeout)
            {
                Thread.Sleep(TICK_MS);
                poll();
            }
        }
    }
}