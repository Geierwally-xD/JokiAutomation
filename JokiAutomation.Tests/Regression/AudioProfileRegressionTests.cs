using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JokiAutomation;
using NUnit.Framework;

namespace JokiAutomation.Tests.Regression
{
    [TestFixture]
    [Category("Regression.Audio")]
    public sealed class AudioProfileRegressionTests
    {
        [Test]
        public void SingleKanzelPredigtRequest_VerificationFailure_IsNotMaskedAndDoesNotRetryCamera()
        {
            int audioCalls = 0;
            int cameraCalls = 0;
            var logs = new List<string>();
            var sut = new PositionControlPtzAudioOrchestrator(
                commandId =>
                {
                    audioCalls++;
                    throw new RasPiCommandFailedException(30, commandId, 40);
                },
                (positionId, movementInput, finalInput) =>
                {
                    cameraCalls++;
                    return Task.CompletedTask;
                },
                logs.Add);

            RasPiCommandFailedException ex = Assert.ThrowsAsync<RasPiCommandFailedException>(async () =>
                await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel" }));

            Assert.That(ex.ExitCode, Is.EqualTo(40));
            Assert.That(audioCalls, Is.EqualTo(1));
            Assert.That(cameraCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task TwoExplicitRequests_ProduceExactlyTwoAudioCalls()
        {
            var audioResults = new Queue<Exception>(new Exception[]
            {
                new RasPiCommandFailedException(30, 114, 40),
                null
            });
            int audioCalls = 0;
            int cameraCalls = 0;
            var sut = new PositionControlPtzAudioOrchestrator(
                commandId =>
                {
                    audioCalls++;
                    Exception next = audioResults.Dequeue();
                    if (next != null)
                    {
                        throw next;
                    }

                    return Task.CompletedTask;
                },
                (positionId, movementInput, finalInput) =>
                {
                    cameraCalls++;
                    return Task.CompletedTask;
                },
                _ => { });

            Assert.ThrowsAsync<RasPiCommandFailedException>(async () =>
                await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel" }));
            await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel" });

            Assert.That(audioCalls, Is.EqualTo(2));
            Assert.That(cameraCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task SuccessOnFirstAttempt_UsesSingleAudioAndSingleCameraCall()
        {
            int audioCalls = 0;
            int cameraCalls = 0;
            var sut = new PositionControlPtzAudioOrchestrator(
                commandId =>
                {
                    audioCalls++;
                    return Task.CompletedTask;
                },
                (positionId, movementInput, finalInput) =>
                {
                    cameraCalls++;
                    return Task.CompletedTask;
                },
                _ => { });

            await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel" });

            Assert.That(audioCalls, Is.EqualTo(1));
            Assert.That(cameraCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task RepeatedExplicitSameProfile_IsNotSkipped()
        {
            int audioCalls = 0;
            var sut = new PositionControlPtzAudioOrchestrator(
                commandId =>
                {
                    audioCalls++;
                    return Task.CompletedTask;
                },
                (positionId, movementInput, finalInput) => Task.CompletedTask,
                _ => { });

            await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel" });
            await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel" });

            Assert.That(audioCalls, Is.EqualTo(2));
        }

        [TestCase("KG", 113)]
        [TestCase("KP", 114)]
        [TestCase("KT", 115)]
        [TestCase("KB", 116)]
        public async Task OtherProfiles_KeepExpectedAudioCommandIds(string profileCode, int expectedCommandId)
        {
            var audioCalls = new List<int>();
            var sut = new PositionControlPtzAudioOrchestrator(
                commandId =>
                {
                    audioCalls.Add(commandId);
                    return Task.CompletedTask;
                },
                (positionId, movementInput, finalInput) => Task.CompletedTask,
                _ => { });

            await sut.ExecuteAsync("Kanzel", profileCode, new[] { "Altar", "Kanzel" });

            Assert.That(audioCalls, Is.EqualTo(new[] { expectedCommandId }));
        }
    }
}
