using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace JokiAutomation.Tests.Hardware
{
    [TestFixture]
    [Category("Unit.Audio")]
    public sealed class AudioControlLogParserTests
    {
        private static string ProfileSuccess(string time, int profile, int attempt = 1)
        {
            return
                $"[{time}] AC_PROFILE_BEGIN pid=1 profile={profile} attempt={attempt} requested=0 actual=unavailable result=0\r\n" +
                $"[{time}] AC_PROFILE_VERIFY pid=1 profile={profile} attempt={attempt} requested=0 actual=0 match=1 result=0\r\n" +
                $"[{time}] AC_PROFILE_SUCCESS pid=1 profile={profile} attempt={attempt} requested=0 actual=0 result=0\r\n";
        }

        [Test]
        public void NormalSuccess_AllFourOperations_AreAccepted()
        {
            string log =
                ProfileSuccess("2026-01-01 10:00:00", 1) +
                ProfileSuccess("2026-01-01 10:00:05", 2) +
                ProfileSuccess("2026-01-01 10:00:10", 1) +
                ProfileSuccess("2026-01-01 10:00:15", 2);

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(4));
            Assert.That(operations.Select(o => o.Profile), Is.EqualTo(new[] { 1, 2, 1, 2 }));
            Assert.That(operations.All(o => o.IsAcceptedSuccess), Is.True);
        }

        [Test]
        public void AllowedRetry_VerifyFailsThenSucceeds_IsAccepted()
        {
            string log =
                "[2026-01-01 10:00:00] AC_PROFILE_BEGIN pid=1 profile=2 attempt=1 requested=0 actual=unavailable result=0\r\n" +
                "[2026-01-01 10:00:01] AC_PROFILE_VERIFY pid=1 profile=2 attempt=1 requested=0 actual=1 match=0 result=0\r\n" +
                "[2026-01-01 10:00:02] AC_PROFILE_RETRY pid=1 profile=2 attempt=1 requested=0 actual=1 result=0\r\n" +
                "[2026-01-01 10:00:03] AC_PROFILE_VERIFY pid=1 profile=2 attempt=2 requested=0 actual=0 match=1 result=0\r\n" +
                "[2026-01-01 10:00:04] AC_PROFILE_SUCCESS pid=1 profile=2 attempt=2 requested=0 actual=0 result=0\r\n";

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].IsAcceptedSuccess, Is.True);
            Assert.That(operations[0].MaximumAttempt, Is.EqualTo(2));
        }

        [Test]
        public void OldSuccessBeforeWindow_IsExcludedByCaller()
        {
            // Window filtering itself is the hardware test's responsibility (by timestamp),
            // but confirm the parser still exposes a usable StartTimestamp for filtering.
            string log = ProfileSuccess("2020-01-01 00:00:00", 1);

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].StartTimestamp, Is.EqualTo(new DateTime(2020, 1, 1)));
        }

        [Test]
        public void WrongOrder_IsDetectableByCaller()
        {
            string log =
                ProfileSuccess("2026-01-01 10:00:00", 1) +
                ProfileSuccess("2026-01-01 10:00:05", 1) +
                ProfileSuccess("2026-01-01 10:00:10", 2) +
                ProfileSuccess("2026-01-01 10:00:15", 2);

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Select(o => o.Profile), Is.Not.EqualTo(new[] { 1, 2, 1, 2 }));
        }

        [Test]
        public void MissingProfile_SequenceIsShorterThanExpected()
        {
            string log =
                ProfileSuccess("2026-01-01 10:00:00", 1) +
                ProfileSuccess("2026-01-01 10:00:05", 2) +
                ProfileSuccess("2026-01-01 10:00:10", 1);

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(3));
            Assert.That(operations.Count, Is.Not.EqualTo(4));
        }

        [Test]
        public void AdditionalUnexpectedOperation_SequenceIsLongerThanExpected()
        {
            string log =
                ProfileSuccess("2026-01-01 10:00:00", 1) +
                ProfileSuccess("2026-01-01 10:00:05", 2) +
                ProfileSuccess("2026-01-01 10:00:10", 1) +
                ProfileSuccess("2026-01-01 10:00:15", 2) +
                ProfileSuccess("2026-01-01 10:00:20", 2);

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(5));
            Assert.That(operations.Count, Is.Not.EqualTo(4));
        }

        [Test]
        public void SuccessWithoutVerify_IsNotAccepted()
        {
            string log =
                "[2026-01-01 10:00:00] AC_PROFILE_BEGIN pid=1 profile=1 attempt=1 requested=0 actual=unavailable result=0\r\n" +
                "[2026-01-01 10:00:01] AC_PROFILE_SUCCESS pid=1 profile=1 attempt=1 requested=0 actual=0 result=0\r\n";

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].IsAcceptedSuccess, Is.False);
            Assert.That(operations[0].HasSuccessfulVerification, Is.False);
        }

        [Test]
        public void VerifyWithoutMatchOne_IsNotAccepted()
        {
            string log =
                "[2026-01-01 10:00:00] AC_PROFILE_BEGIN pid=1 profile=1 attempt=1 requested=0 actual=unavailable result=0\r\n" +
                "[2026-01-01 10:00:01] AC_PROFILE_VERIFY pid=1 profile=1 attempt=1 requested=0 actual=1 match=0 result=0\r\n" +
                "[2026-01-01 10:00:02] AC_PROFILE_SUCCESS pid=1 profile=1 attempt=1 requested=0 actual=1 result=0\r\n";

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].IsAcceptedSuccess, Is.False);
        }

        [Test]
        public void ProfileFail_IsNotAccepted()
        {
            string log =
                "[2026-01-01 10:00:00] AC_PROFILE_BEGIN pid=1 profile=1 attempt=1 requested=0 actual=unavailable result=0\r\n" +
                "[2026-01-01 10:00:01] AC_PROFILE_VERIFY pid=1 profile=1 attempt=1 requested=0 actual=1 match=0 result=0\r\n" +
                "[2026-01-01 10:00:02] AC_PROFILE_FAIL pid=1 profile=1 attempt=1 requested=0 actual=1 result=40\r\n";

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].HasFailureMarker, Is.True);
            Assert.That(operations[0].IsAcceptedSuccess, Is.False);
        }

        [Test]
        public void MoreThanOneRetry_IsNotAccepted()
        {
            string log =
                "[2026-01-01 10:00:00] AC_PROFILE_BEGIN pid=1 profile=1 attempt=1 requested=0 actual=unavailable result=0\r\n" +
                "[2026-01-01 10:00:01] AC_PROFILE_VERIFY pid=1 profile=1 attempt=1 requested=0 actual=1 match=0 result=0\r\n" +
                "[2026-01-01 10:00:02] AC_PROFILE_RETRY pid=1 profile=1 attempt=1 requested=0 actual=1 result=0\r\n" +
                "[2026-01-01 10:00:03] AC_PROFILE_VERIFY pid=1 profile=1 attempt=2 requested=0 actual=1 match=0 result=0\r\n" +
                "[2026-01-01 10:00:04] AC_PROFILE_RETRY pid=1 profile=1 attempt=2 requested=0 actual=1 result=0\r\n" +
                "[2026-01-01 10:00:05] AC_PROFILE_VERIFY pid=1 profile=1 attempt=3 requested=0 actual=0 match=1 result=0\r\n" +
                "[2026-01-01 10:00:06] AC_PROFILE_SUCCESS pid=1 profile=1 attempt=3 requested=0 actual=0 result=0\r\n";

            IReadOnlyList<AudioControlProfileOperation> operations = AudioControlLogParser.Parse(log);

            Assert.That(operations.Count, Is.EqualTo(1));
            Assert.That(operations[0].MaximumAttempt, Is.EqualTo(3));
            Assert.That(operations[0].IsAcceptedSuccess, Is.False);
        }
    }
}
