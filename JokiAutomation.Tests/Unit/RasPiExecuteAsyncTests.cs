using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JokiAutomation;
using JokiAutomation.Tests.Fakes;
using NUnit.Framework;

namespace JokiAutomation.Tests.Unit
{
    [TestFixture]
    [Category("Unit.Audio")]
    public sealed class RasPiExecuteAsyncTests
    {
        [Test]
        public async Task RasPiExecuteAsync_ExitCodeZero_CompletesSuccessfully()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor
            {
                Result = new RasPiCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "AC_PROFILE_SUCCESS profile=2 result=0",
                    StandardError = string.Empty
                }
            };

            RasPi sut = CreateSubject(fakeExecutor, out List<string> logs);

            await sut.RasPiExecuteAsync(30, 114, TimeSpan.FromSeconds(5));

            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
            Assert.That(logs.Any(log => log.Contains("[RASPI_ASYNC_END]") && log.Contains("ExitCode=0")), Is.True);
        }

        [Test]
        public void RasPiExecuteAsync_ExitCode40_ThrowsTypedFailure()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor
            {
                Result = new RasPiCommandExecutionResult
                {
                    ExitCode = 40,
                    StandardOutput = "AC_PROFILE_FAIL profile=2 result=40",
                    StandardError = string.Empty
                }
            };

            RasPi sut = CreateSubject(fakeExecutor, out List<string> logs);

            RasPiCommandFailedException ex = Assert.ThrowsAsync<RasPiCommandFailedException>(async () =>
                await sut.RasPiExecuteAsync(30, 114, TimeSpan.FromSeconds(5)));

            Assert.That(ex.Command, Is.EqualTo(30));
            Assert.That(ex.Id, Is.EqualTo(114));
            Assert.That(ex.ExitCode, Is.EqualTo(40));
            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
            Assert.That(logs.Any(log => log.Contains("[RASPI_ASYNC_FAIL]") && log.Contains("Command=30") && log.Contains("Position=114") && log.Contains("ExitCode=40")), Is.True);
            Assert.That(logs.Any(log => log.Contains("[RASPI_ASYNC_END]")), Is.False);
            Assert.That(logs.Any(log => log.Contains("superSecretPassword")), Is.False);
        }

        [TestCase(31)]
        [TestCase(32)]
        [TestCase(38)]
        [TestCase(39)]
        public void RasPiExecuteAsync_NonZeroExitCode_PropagatesFailure(int exitCode)
        {
            var fakeExecutor = new FakeRasPiCommandExecutor
            {
                Result = new RasPiCommandExecutionResult
                {
                    ExitCode = exitCode,
                    StandardOutput = "AC_PROFILE_FAIL",
                    StandardError = string.Empty
                }
            };

            RasPi sut = CreateSubject(fakeExecutor, out List<string> logs);

            RasPiCommandFailedException ex = Assert.ThrowsAsync<RasPiCommandFailedException>(async () =>
                await sut.RasPiExecuteAsync(30, 114, TimeSpan.FromSeconds(5)));

            Assert.That(ex.ExitCode, Is.EqualTo(exitCode));
            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
            Assert.That(logs.Any(log => log.Contains("[RASPI_ASYNC_END]")), Is.False);
        }

        [Test]
        public void RasPiExecuteAsync_MissingExitStatus_IsFailure()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor
            {
                Result = new RasPiCommandExecutionResult
                {
                    ExitCode = null,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty
                }
            };

            RasPi sut = CreateSubject(fakeExecutor, out _);

            RasPiCommandFailedException ex = Assert.ThrowsAsync<RasPiCommandFailedException>(async () =>
                await sut.RasPiExecuteAsync(30, 114, TimeSpan.FromSeconds(5)));

            Assert.That(ex.ExitCode, Is.Null);
            Assert.That(ex.Message, Does.Contain("no exit status"));
            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
        }

        [Test]
        public void RasPiExecuteAsync_SshFailure_Propagates()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor
            {
                ExceptionToThrow = new InvalidOperationException("SSH connect failed")
            };

            RasPi sut = CreateSubject(fakeExecutor, out List<string> logs);

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await sut.RasPiExecuteAsync(30, 114, TimeSpan.FromSeconds(5)));

            Assert.That(ex.Message, Is.EqualTo("SSH connect failed"));
            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
            Assert.That(logs.Any(log => log.Contains("PROFILE_SUCCESS")), Is.False);
        }

        [Test]
        public void RasPiExecuteAsync_Timeout_Propagates()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor
            {
                ExceptionToThrow = new TimeoutException("timeout")
            };

            RasPi sut = CreateSubject(fakeExecutor, out List<string> logs);

            TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(async () =>
                await sut.RasPiExecuteAsync(30, 114, TimeSpan.FromSeconds(5)));

            Assert.That(ex.Message, Is.EqualTo("timeout"));
            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
            Assert.That(logs.Any(log => log.Contains("[RASPI_ASYNC_END]")), Is.False);
        }

        [Test]
        public void RasPiExecuteAsync_Cancellation_PropagatesWithoutRetry()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor
            {
                Delay = TimeSpan.FromMilliseconds(200)
            };

            RasPi sut = CreateSubject(fakeExecutor, out List<string> logs);
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(50);

                Assert.That(async () =>
                        await sut.RasPiExecuteAsync(30, 114, TimeSpan.FromSeconds(5), cts.Token),
                    Throws.InstanceOf<OperationCanceledException>());
            }

            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
            Assert.That(logs.Any(log => log.Contains("CANCELED")), Is.True);
            Assert.That(logs.Any(log => log.Contains("[RASPI_ASYNC_END]")), Is.False);
        }

        [Test]
        public async Task RasPiExecuteAsync_PredigtCommand_UsesExpectedRemoteCommand()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor();
            RasPi sut = CreateSubject(fakeExecutor, out List<string> logs, "superSecretPassword");

            await sut.RasPiExecuteAsync(30, 114, TimeSpan.FromSeconds(5));

            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
            string remoteCommand = fakeExecutor.Invocations[0].Command;
            bool matchesExpected = remoteCommand.Contains("sudo -S /home/pi/JokiAutomation/RasPiAutomation 30 114")
                && remoteCommand.Contains("echo '");
            Assert.That(matchesExpected, Is.True, "Remote command format mismatch.");
            Assert.That(logs.Any(log => log.Contains("superSecretPassword")), Is.False);
        }

        private static RasPi CreateSubject(FakeRasPiCommandExecutor fakeExecutor, out List<string> logs, string password = "superSecretPassword")
        {
            logs = new List<string>();
            var sut = new RasPi(fakeExecutor);
            sut.SetConnectionSettingsForTests("192.0.2.10", "pi", password);
            sut.SetLogSinkForTests(logs.Add);
            return sut;
        }
    }
}
