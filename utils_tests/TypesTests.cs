namespace Keel.Utils.Tests
{
    public partial class TypesTests
    {
        [Fact]
        public void Lookup_MoreChildTypesThanByteIndices_ThrowsDescriptiveError()
        {
            // Child types are indexed with a byte, and IManyChildren has 257 of them - see TypesTests.ManyChildren.cs.
            var exception = Assert.Throws<TypeInitializationException>(() => Types.Lookup<IManyChildren>.Value);

            var innerException = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("Too many child types", innerException.Message);
        }

        [Fact]
        public void IsBlittable_ConcurrentFirstCalls_DoNotThrow()
        {
            // Types nothing has asked about yet, so every call below goes on to fill the cache.
            var valueTypes = typeof(int).Assembly.GetTypes()
                .Where(type => type.IsValueType && type.IsPublic)
                .ToArray();

            const int threadCount = 8;
            using var startTogether = new Barrier(threadCount);

            var threads = Enumerable.Range(0, threadCount)
                .Select(_ => Task.Factory.StartNew(() =>
                {
                    startTogether.SignalAndWait();
                    foreach (var type in valueTypes)
                        TypeTraits.IsBlittable(type);
                }, TaskCreationOptions.LongRunning))
                .ToArray();

            Task.WaitAll(threads);
        }
    }
}