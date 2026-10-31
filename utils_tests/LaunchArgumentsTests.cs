using System.Globalization;

namespace Keel.Utils.Tests
{
    public class LaunchArgumentsTests
    {
        [Fact]
        public void CreateInstance_ParsesFractionsRegardlessOfCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                // In German '.' is the group separator, so a culture aware parse reads '0.5' as 5.
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var arguments = new Dictionary<string, List<string>> { ["ratio"] = ["0.5"] };
                var settings = LaunchArguments.CreateInstance<Settings>(arguments);

                Assert.Equal(0.5f, settings.Ratio);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        private sealed class Settings
        {
            [InjectCommandArgument("ratio")] public float Ratio = -1f;
        }
    }
}