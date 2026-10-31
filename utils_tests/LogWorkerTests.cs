using Keel.Utils.Debug;
using Keel.Utils.Threading;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Keel.Utils.Tests
{
    /// <summary>
    /// Both tests redirect the console, which is process wide - keeping them in one class keeps them from running at the same time.
    /// </summary>
    public class LogWorkerTests
    {
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

        [Fact]
        public void Stop_PrintsLogsThatAreStillQueued()
        {
            var output = RunWithConsoleCaptured(readOutput =>
            {
                var worker = LogWorker.BuildAndStart(WorkerScope.NewBackgroundThread);

                // Once this is printed, the worker is between updates.
                worker.Info("warm up");
                Assert.True(SpinWait.SpinUntil(() => readOutput().Contains("warm up"), s_timeout), "The log worker never printed.");

                // Queued right before stopping. The worker checks for the stop before its next update, so no regular
                // update is left to print this in.
                worker.Info("last words");
                StopAndWait(worker);
            });

            Assert.Contains("last words", output);
        }

        [Fact]
        public void FileWriteFailure_IsReportedOnce()
        {
            var logDirectory = Path.Combine(Path.GetTempPath(), $"keel_log_worker_{Guid.NewGuid():N}");
            try
            {
                var output = RunWithConsoleCaptured(_ =>
                {
                    var worker = LogWorker.BuildAndStart(WorkerScope.NewBackgroundThread, logDirectory);

                    // Break the file output the way a lost disk would: the next write to it throws.
                    var fileWriter = (StreamWriter)typeof(LogWorker)
                        .GetField("_fileWriter", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(worker);
                    fileWriter!.Dispose();

                    // The file is written about once a second. Wait for that write to fail, then leave time for a repeat.
                    worker.Info("before failure");
                    Thread.Sleep(1500);
                    worker.Info("after failure");
                    Thread.Sleep(200);

                    StopAndWait(worker);
                });

                Assert.Single(Regex.Matches(output, "failed to write logs to file"));
            }
            finally
            {
                if (Directory.Exists(logDirectory))
                    Directory.Delete(logDirectory, recursive: true);
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> with the console redirected, giving it a way to read what was printed so far.
        /// </summary>
        private static string RunWithConsoleCaptured(Action<Func<string>> action)
        {
            var originalOutput = Console.Out;
            var output = new StringWriter();
            var synchronizedOutput = TextWriter.Synchronized(output);
            Console.SetOut(synchronizedOutput);
            try
            {
                action(ReadOutput);
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            return ReadOutput();

            // The synchronized writer locks on itself, so reading under the same lock never sees a write half done.
            string ReadOutput()
            {
                lock (synchronizedOutput)
                    return output.ToString();
            }
        }

        private static void StopAndWait(LogWorker worker)
        {
            using var stopped = new ManualResetEventSlim();
            worker.Stopped += stopped.Set;
            worker.Stop();

            Assert.True(stopped.Wait(s_timeout), "The log worker didn't stop.");
        }
    }
}