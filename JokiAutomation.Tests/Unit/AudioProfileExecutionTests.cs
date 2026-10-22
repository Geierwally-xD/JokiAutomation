using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JokiAutomation;
using JokiAutomation.Tests.Fakes;
using NUnit.Framework;

namespace JokiAutomation.Tests.Unit
{
    [TestFixture]
    [Category("Unit.Audio")]
    public sealed class AudioProfileExecutionTests
    {
        [Test]
        public async Task PredigtProfile_ExecutesExpectedAudioCommand()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor();
            RasPi rasPi = CreateRasPi(fakeExecutor);
            var audioMix = new AudioMix { _rasPi = rasPi };
            PositionControlProfileMapping mapping = PositionControlProfileMapper.MapOrThrow("KP");

            await audioMix.ExecuteAudioAsync(mapping.AudioCommandId);

            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
            bool commandMatches = fakeExecutor.Invocations[0].Command.Contains("/home/pi/JokiAutomation/RasPiAutomation 30 114");
            Assert.That(commandMatches, Is.True, "Unexpected Raspberry Pi audio command.");
        }

        [Test]
        public void PredigtProfile_VerificationFailure_IsPropagated()
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

            RasPi rasPi = CreateRasPi(fakeExecutor);
            var audioMix = new AudioMix { _rasPi = rasPi };
            PositionControlProfileMapping mapping = PositionControlProfileMapper.MapOrThrow("KP");

            RasPiCommandFailedException ex = Assert.ThrowsAsync<RasPiCommandFailedException>(async () =>
                await audioMix.ExecuteAudioAsync(mapping.AudioCommandId));

            Assert.That(ex.ExitCode, Is.EqualTo(40));
            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TwoExplicitAudioRequests_ProduceTwoAudioDispatches()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor();
            RasPi rasPi = CreateRasPi(fakeExecutor);
            var audioMix = new AudioMix { _rasPi = rasPi };
            PositionControlProfileMapping mapping = PositionControlProfileMapper.MapOrThrow("KP");

            await audioMix.ExecuteAudioAsync(mapping.AudioCommandId);
            await audioMix.ExecuteAudioAsync(mapping.AudioCommandId);

            Assert.That(fakeExecutor.Invocations, Has.Count.EqualTo(2));
        }

        private static RasPi CreateRasPi(FakeRasPiCommandExecutor fakeExecutor)
        {
            var logs = new List<string>();
            var rasPi = new RasPi(fakeExecutor);
            rasPi.SetConnectionSettingsForTests("192.0.2.10", "pi", "test-password");
            rasPi.SetLogSinkForTests(logs.Add);
            return rasPi;
        }
    }
}
