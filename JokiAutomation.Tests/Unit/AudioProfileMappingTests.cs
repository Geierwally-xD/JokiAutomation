using JokiAutomation;
using NUnit.Framework;

namespace JokiAutomation.Tests.Unit
{
    [TestFixture]
    [Category("Unit.Audio")]
    public sealed class AudioProfileMappingTests
    {
        [TestCase("GG", 1, 113)]
        [TestCase("GP", 2, 114)]
        [TestCase("KP", 2, 114)]
        [TestCase("GT", 3, 115)]
        [TestCase("GB", 4, 116)]
        [TestCase("gp", 2, 114)]
        public void AudioProfileCode_MapsToExpectedCommand(string code, int expectedProfileId, int expectedCommandId)
        {
            bool mapped = PositionControlProfileMapper.TryMap(code, out PositionControlProfileMapping mapping);

            Assert.That(mapped, Is.True);
            Assert.That(mapping, Is.Not.Null);
            Assert.That(mapping.AudioProfileId, Is.EqualTo(expectedProfileId));
            Assert.That(mapping.AudioCommandId, Is.EqualTo(expectedCommandId));
        }

        [Test]
        public void CameraCode_DoesNotChangeAudioProfileMapping()
        {
            Assert.That(PositionControlProfileMapper.TryMap("GP", out PositionControlProfileMapping goproPredigt), Is.True);
            Assert.That(PositionControlProfileMapper.TryMap("KP", out PositionControlProfileMapping kanzelPredigt), Is.True);
            Assert.That(PositionControlProfileMapper.TryMap("GG", out PositionControlProfileMapping goproGottesdienst), Is.True);

            Assert.That(goproPredigt.AudioProfileId, Is.EqualTo(kanzelPredigt.AudioProfileId));
            Assert.That(goproPredigt.AudioCommandId, Is.EqualTo(kanzelPredigt.AudioCommandId));
            Assert.That(goproPredigt.AudioProfileId, Is.Not.EqualTo(goproGottesdienst.AudioProfileId));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("G")]
        [TestCase("GPP")]
        [TestCase("GX")]
        [TestCase("?P")]
        [TestCase(" P")]
        public void InvalidAudioProfileCode_IsRejected(string code)
        {
            bool mapped = PositionControlProfileMapper.TryMap(code, out PositionControlProfileMapping mapping);

            Assert.That(mapped, Is.False);
            Assert.That(mapping, Is.Null);
            Assert.Throws<System.ArgumentException>(() => PositionControlProfileMapper.MapOrThrow(code));
        }
    }
}
