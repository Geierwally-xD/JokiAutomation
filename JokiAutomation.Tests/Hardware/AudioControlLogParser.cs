using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JokiAutomation.Tests.Hardware
{
    /// <summary>
    /// Parses the Raspberry Pi "AudioControl.log" lines into discrete profile-switch operations.
    /// Pure/testable logic, no SSH/IO dependency.
    /// </summary>
    internal static class AudioControlLogParser
    {
        // Example real log lines:
        // [2026-10-21 17:38:55] AC_PROFILE_BEGIN pid=2441 profile=2 attempt=1 requested=0,50,0,31 actual=unavailable result=0
        // [2026-10-21 17:38:56] AC_PROFILE_VERIFY pid=2441 profile=2 attempt=1 requested=0,50,0,31 actual=0,50,0,31 match=1 result=0
        // [2026-10-21 17:38:56] AC_PROFILE_SUCCESS pid=2441 profile=2 attempt=1 requested=0,50,0,31 actual=0,50,0,31 result=0
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

        private static readonly Regex LineRegex = new Regex(
            @"^\[(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]\s+(?<marker>AC_PROFILE_[A-Z_]+)(?<rest>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex ProfileRegex = new Regex(@"\bprofile=(?<value>-?\d+)\b", RegexOptions.Compiled);
        private static readonly Regex AttemptRegex = new Regex(@"\battempt=(?<value>-?\d+)\b", RegexOptions.Compiled);
        private static readonly Regex MatchRegex = new Regex(@"\bmatch=(?<value>-?\d+)\b", RegexOptions.Compiled);

        internal static IReadOnlyList<AudioControlProfileOperation> Parse(string logText)
        {
            var operations = new List<AudioControlProfileOperation>();
            if (string.IsNullOrEmpty(logText))
            {
                return operations;
            }

            AudioControlProfileOperation current = null;
            var currentLines = new List<string>();

            string[] lines = logText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                Match match = LineRegex.Match(rawLine);
                if (!match.Success)
                {
                    continue;
                }

                if (!DateTime.TryParseExact(
                        match.Groups["timestamp"].Value,
                        TimestampFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime timestamp))
                {
                    continue;
                }

                string marker = match.Groups["marker"].Value;
                string rest = match.Groups["rest"].Value;

                if (marker == "AC_PROFILE_BEGIN")
                {
                    if (current != null)
                    {
                        current.RawLines = currentLines.ToArray();
                        operations.Add(current);
                    }

                    current = new AudioControlProfileOperation
                    {
                        StartTimestamp = timestamp,
                        Profile = ExtractInt(rest, ProfileRegex) ?? 0
                    };
                    currentLines = new List<string> { rawLine };
                    continue;
                }

                if (current == null)
                {
                    // Ignore markers that appear outside of a recognized operation.
                    continue;
                }

                currentLines.Add(rawLine);

                int? attempt = ExtractInt(rest, AttemptRegex);
                if (attempt.HasValue && attempt.Value > current.MaximumAttempt)
                {
                    current.MaximumAttempt = attempt.Value;
                }

                switch (marker)
                {
                    case "AC_PROFILE_VERIFY":
                        int? matchValue = ExtractInt(rest, MatchRegex);
                        if (matchValue.HasValue && matchValue.Value == 1)
                        {
                            current.HasSuccessfulVerification = true;
                        }

                        break;

                    case "AC_PROFILE_SUCCESS":
                        current.HasSuccessMarker = true;
                        current.EndTimestamp = timestamp;
                        current.RawLines = currentLines.ToArray();
                        operations.Add(current);
                        current = null;
                        currentLines = new List<string>();
                        break;

                    case "AC_PROFILE_FAIL":
                        current.HasFailureMarker = true;
                        current.EndTimestamp = timestamp;
                        current.RawLines = currentLines.ToArray();
                        operations.Add(current);
                        current = null;
                        currentLines = new List<string>();
                        break;
                }
            }

            if (current != null)
            {
                current.RawLines = currentLines.ToArray();
                operations.Add(current);
            }

            return operations;
        }

        private static int? ExtractInt(string text, Regex regex)
        {
            Match match = regex.Match(text);
            if (!match.Success)
            {
                return null;
            }

            if (int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }

            return null;
        }
    }

    internal sealed class AudioControlProfileOperation
    {
        public DateTime StartTimestamp { get; set; }

        public DateTime? EndTimestamp { get; set; }

        public int Profile { get; set; }

        public bool HasSuccessfulVerification { get; set; }

        public bool HasSuccessMarker { get; set; }

        public bool HasFailureMarker { get; set; }

        public int MaximumAttempt { get; set; }

        public IReadOnlyList<string> RawLines { get; set; } = Array.Empty<string>();

        /// <summary>
        /// A profile operation only counts as a successful hardware-verified switch when it has
        /// a verified match (match=1) AND an explicit success marker AND no failure marker AND
        /// did not require more than 2 attempts.
        /// </summary>
        public bool IsAcceptedSuccess =>
            HasSuccessfulVerification
            && HasSuccessMarker
            && !HasFailureMarker
            && MaximumAttempt <= 2;
    }
}
