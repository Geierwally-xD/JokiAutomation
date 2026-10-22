using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JokiAutomation;
using NUnit.Framework;

namespace JokiAutomation.Tests.Integration
{
    [TestFixture]
    [Category("Integration.Audio")]
    public sealed class PositionControlAudioIntegrationTests
    {
        [Test]
        public async Task KanzelMitPredigt_RequestsPredigtAudioAndKanzelCamera()
        {
            var audioCalls = new List<int>();
            var cameraCalls = new List<(int PositionId, Form1.ATEMInput MovementInput, Form1.ATEMInput FinalInput)>();
            var sut = new PositionControlPtzAudioOrchestrator(
                commandId =>
                {
                    audioCalls.Add(commandId);
                    return Task.CompletedTask;
                },
                (positionId, movementInput, finalInput) =>
                {
                    cameraCalls.Add((positionId, movementInput, finalInput));
                    return Task.CompletedTask;
                },
                _ => { });

            PositionControlPtzExecutionResult result = await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel", "Orgel" });

            Assert.That(audioCalls, Is.EqualTo(new[] { 114 }));
            Assert.That(cameraCalls, Has.Count.EqualTo(1));
            Assert.That(cameraCalls[0].PositionId, Is.EqualTo(1));
            Assert.That(cameraCalls[0].MovementInput, Is.EqualTo(Form1.ATEMInput.CanonPtzPreacher));
            Assert.That(result.ProfileMapping.AudioProfileId, Is.EqualTo(2));
        }

        [Test]
        public async Task KanzelMitGottesdienst_UsesGottesdienstAudio()
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

            PositionControlPtzExecutionResult result = await sut.ExecuteAsync("Kanzel", "KG", new[] { "Altar", "Kanzel" });

            Assert.That(audioCalls, Is.EqualTo(new[] { 113 }));
            Assert.That(result.ProfileMapping.AudioProfileId, Is.EqualTo(1));
            Assert.That(result.ProfileMapping.AudioCommandId, Is.Not.EqualTo(114));
        }

        [Test]
        public void AudioVerificationFailure_PreventsCameraSequenceAndPropagates()
        {
            int audioCalls = 0;
            int cameraCalls = 0;
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
                _ => { });

            RasPiCommandFailedException ex = Assert.ThrowsAsync<RasPiCommandFailedException>(async () =>
                await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel" }));

            Assert.That(ex.ExitCode, Is.EqualTo(40));
            Assert.That(audioCalls, Is.EqualTo(1));
            Assert.That(cameraCalls, Is.EqualTo(0));
        }

        [Test]
        public void PositionFailure_AfterSuccessfulAudio_IsPropagated()
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
                    throw new InvalidOperationException("camera move failed");
                },
                _ => { });

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await sut.ExecuteAsync("Kanzel", "KP", new[] { "Altar", "Kanzel" }));

            Assert.That(ex.Message, Is.EqualTo("camera move failed"));
            Assert.That(audioCalls, Is.EqualTo(1));
            Assert.That(cameraCalls, Is.EqualTo(1));
        }
    }
}
