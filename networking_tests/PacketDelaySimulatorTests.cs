using Keel.Networking.Udp.LowLevel.Simulators;

namespace Keel.Networking.Tests
{
    /// <summary>
    /// The minimum and maximum delay depend on each other, so setting both has to give the same result in either
    /// order - an object initializer shouldn't have to know which one to set first.
    /// </summary>
    public class PacketDelaySimulatorTests
    {
        [Fact]
        public void RaisingBothAboveDefaults_WorksInEitherOrder()
        {
            var minFirst = new PacketDelaySimulator { PacketMinDelayMs = 300, PacketMaxDelayMs = 500 };
            var maxFirst = new PacketDelaySimulator { PacketMaxDelayMs = 500, PacketMinDelayMs = 300 };

            Assert.Equal((300f, 500f), (minFirst.PacketMinDelayMs, minFirst.PacketMaxDelayMs));
            Assert.Equal((300f, 500f), (maxFirst.PacketMinDelayMs, maxFirst.PacketMaxDelayMs));
        }

        [Fact]
        public void LoweringBothBelowDefaults_WorksInEitherOrder()
        {
            var minFirst = new PacketDelaySimulator { PacketMinDelayMs = 20, PacketMaxDelayMs = 50 };
            var maxFirst = new PacketDelaySimulator { PacketMaxDelayMs = 50, PacketMinDelayMs = 20 };

            Assert.Equal((20f, 50f), (minFirst.PacketMinDelayMs, minFirst.PacketMaxDelayMs));
            Assert.Equal((20f, 50f), (maxFirst.PacketMinDelayMs, maxFirst.PacketMaxDelayMs));
        }

        [Fact]
        public void LastValueSetWins_AndMinimumNeverExceedsMaximum()
        {
            var simulator = new PacketDelaySimulator { PacketMinDelayMs = 300, PacketMaxDelayMs = 100 };

            Assert.Equal(100f, simulator.PacketMaxDelayMs);
            Assert.Equal(100f, simulator.PacketMinDelayMs);
        }
    }
}