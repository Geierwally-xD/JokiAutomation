using System;
using System.IO;

namespace CanonPtzCommon
{
    /// <summary>
    /// Shared state for tracking the last recalled preset across applications
    /// Both JokiAutomation and CanonRemoteControl can write/read this state
    /// </summary>
    public static class SharedPresetState
    {
        private static readonly string StateFilePath = Path.Combine(
            Path.GetTempPath(), 
            "CanonPtzLastPreset.txt");

        /// <summary>
        /// Write the last recalled preset number to shared state
        /// </summary>
        /// <param name="presetNumber">The preset number that was recalled (1-based)</param>
        public static void SetLastPreset(int presetNumber)
        {
            try
            {
                File.WriteAllText(StateFilePath, presetNumber.ToString());
            }
            catch
            {
                // Silently fail if we can't write the state file
            }
        }

        /// <summary>
        /// Read the last recalled preset number from shared state
        /// </summary>
        /// <returns>The last preset number, or 1 as default if not available</returns>
        public static int GetLastPreset()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    string content = File.ReadAllText(StateFilePath).Trim();
                    if (int.TryParse(content, out int preset))
                    {
                        return preset;
                    }
                }
            }
            catch
            {
                // Silently fail if we can't read the state file
            }

            return 1; // Default to preset 1
        }

        /// <summary>
        /// Get the path to the shared state file (for debugging)
        /// </summary>
        public static string GetStateFilePath()
        {
            return StateFilePath;
        }
    }
}
