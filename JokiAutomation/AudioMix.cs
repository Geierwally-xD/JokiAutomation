using System;
using System.IO;

namespace JokiAutomation
{
    /// <summary>
    /// Audio mixing control class for managing audio profiles and channels
    /// Interfaces with Raspberry Pi for audio control operations
    /// </summary>
    internal class AudioMix : IDisposable
    {
        private const int MaxProfiles = 10;
        private const int ChannelsPerProfile = 4;
        private const int AM_EXECUTE = 30;  // Audio Mix command
        private const int AM_TEACH = 31;    // Audio Mix teach profile

        public const int AM_ACTIVE = 0x10;
        public const int AM_VOLUME = 0x20;
        public const int AM_FADEUP = 0x30;
        public const int AM_FADEDOWN = 0x40;
        public const int AM_FADESTOP = 0x50;
        public const int AM_AUDIO_RESET = 0x60;
        public const int AM_PROFILE = 0x70; // execute audio profile 
        public const int AM_SEQUENCE = 50;  // command sequence raspberry pi

        public bool[] channelActive_ = new bool[] { true, true, true, true };
        public RasPi _rasPi = new RasPi();  // Raspberry Pi functionality

        internal byte[,] audioProfile = new byte[MaxProfiles, ChannelsPerProfile];
        private string JokiAutomationPath = Environment.GetEnvironmentVariable("JokiAutomation");

        /// <summary>
        /// Initialize audio control system
        /// Reads audio profiles from configuration file and initializes Raspberry Pi
        /// </summary>
        /// <param name="winForm">Parent form for logging and UI updates</param>
        public void initAudio(Form1 winForm)
        {
            try
            {
                _rasPi.initRasPi(winForm);

                // Read in audio profiles
                string configPath = JokiAutomationPath + "Audio.cfg";

                if (File.Exists(configPath))
                {
                    // Try to load with new format first
                    if (!TryLoadAudioProfileNew(configPath))
                    {
                        // Fall back to old format and migrate
                        if (TryLoadAudioProfileLegacy(configPath))
                        {
                            // Backup old file
                            string backupPath = configPath + ".old";
                            File.Copy(configPath, backupPath, true);

                            // Save in new format
                            SaveAudioProfile(configPath);

                            System.Diagnostics.Debug.WriteLine($"Migrated Audio.cfg to new format. Backup saved as Audio.cfg.old");
                        }
                        else
                        {
                            // Both formats failed, use defaults
                            InitializeDefaultProfile();
                        }
                    }
                }
                else
                {
                    InitializeDefaultProfile();
                }
            }
            catch (Exception ex)
            {
                // Log error and initialize with defaults
                System.Diagnostics.Debug.WriteLine($"Error initializing audio: {ex.Message}");
                InitializeDefaultProfile();
            }
        }

        /// <summary>
        /// Try to load audio profile using new BinaryReader format
        /// </summary>
        /// <param name="filePath">Path to audio configuration file</param>
        /// <returns>True if load successful, false otherwise</returns>
        private bool TryLoadAudioProfileNew(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    // Check if file has header (new format)
                    if (stream.Length < 8)
                    {
                        return false;
                    }

                    // Read dimensions for validation
                    int rows = reader.ReadInt32();
                    int cols = reader.ReadInt32();

                    // Validate dimensions match expected profile size
                    if (rows != MaxProfiles || cols != ChannelsPerProfile)
                    {
                        return false;
                    }

                    // Read profile data
                    for (int i = 0; i < MaxProfiles; i++)
                    {
                        for (int j = 0; j < ChannelsPerProfile; j++)
                        {
                            audioProfile[i, j] = reader.ReadByte();
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Try to load audio profile using legacy BinaryFormatter format
        /// Used for backward compatibility and automatic migration
        /// </summary>
        /// <param name="filePath">Path to audio configuration file</param>
        /// <returns>True if load successful, false otherwise</returns>
        private bool TryLoadAudioProfileLegacy(string filePath)
        {
            try
            {
                using (Stream stream = File.Open(filePath, FileMode.Open))
                {
#pragma warning disable SYSLIB0011 // BinaryFormatter is obsolete - used only for migration
                    var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                    byte[,] loadedProfile = (byte[,])binaryFormatter.Deserialize(stream);
#pragma warning restore SYSLIB0011

                    // Validate dimensions
                    if (loadedProfile.GetLength(0) != MaxProfiles || loadedProfile.GetLength(1) != ChannelsPerProfile)
                    {
                        return false;
                    }

                    // Copy to audioProfile
                    Array.Copy(loadedProfile, audioProfile, loadedProfile.Length);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Initialize default profile values (all zeros)
        /// </summary>
        private void InitializeDefaultProfile()
        {
            for (int i = 0; i < MaxProfiles; i++)
            {
                for (int j = 0; j < ChannelsPerProfile; j++)
                {
                    audioProfile[i, j] = 0;
                }
            }
        }

        /// <summary>
        /// Execute audio mix command with specified profile ID
        /// </summary>
        /// <param name="ID">Profile ID to execute (0-9)</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when ID is out of valid range</exception>
        public void executeAudio(int ID)
        {
            if (!ValidateProfileID(ID))
            {
                throw new ArgumentOutOfRangeException(nameof(ID), $"ID must be between 0 and {MaxProfiles - 1}");
            }

            _rasPi.rasPiExecute(AM_EXECUTE, ID);
        }

        /// <summary>
        /// Teach audio mix profile to Raspberry Pi
        /// Saves profile to file and transmits to hardware
        /// </summary>
        /// <param name="ID">Profile ID to teach (0-9)</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when ID is out of valid range</exception>
        /// <exception cref="IOException">Thrown when file operations fail</exception>
        public void teachAudio(int ID)
        {
            if (!ValidateProfileID(ID))
            {
                throw new ArgumentOutOfRangeException(nameof(ID), $"ID must be between 0 and {MaxProfiles - 1}");
            }

            try
            {
                string configPath = JokiAutomationPath + "Audio.cfg";
                SaveAudioProfile(configPath);

                int teachSequence = BuildTeachSequence(ID);
                _rasPi.rasPiExecute(AM_TEACH, teachSequence);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to teach audio profile {ID}", ex);
            }
        }

        /// <summary>
        /// Save audio profile using BinaryWriter (secure alternative to BinaryFormatter)
        /// </summary>
        /// <param name="filePath">Path where to save audio configuration</param>
        private void SaveAudioProfile(string filePath)
        {
            using (FileStream stream = File.Open(filePath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                // Write dimensions for validation on load
                writer.Write(MaxProfiles);
                writer.Write(ChannelsPerProfile);

                // Write profile data
                for (int i = 0; i < MaxProfiles; i++)
                {
                    for (int j = 0; j < ChannelsPerProfile; j++)
                    {
                        writer.Write(audioProfile[i, j]);
                    }
                }
            }
        }

        /// <summary>
        /// Build teach sequence from profile data
        /// Encodes all 4 channels into a single integer value
        /// </summary>
        /// <param name="ID">Profile ID to encode</param>
        /// <returns>Encoded teach sequence value</returns>
        private int BuildTeachSequence(int ID)
        {
            int teachSequence = 0;
            teachSequence |= audioProfile[ID, 0];
            teachSequence |= audioProfile[ID, 1] << 4;
            teachSequence |= audioProfile[ID, 2] << 8;
            teachSequence |= audioProfile[ID, 3] << 12;
            teachSequence |= ID << 16;
            return teachSequence;
        }

        /// <summary>
        /// Validate profile ID is within valid range
        /// </summary>
        /// <param name="ID">Profile ID to validate</param>
        /// <returns>True if ID is valid (0-9), false otherwise</returns>
        private bool ValidateProfileID(int ID)
        {
            return ID >= 0 && ID < MaxProfiles;
        }

        /// <summary>
        /// Get profile value at specified position (safe access with validation)
        /// </summary>
        /// <param name="profileID">Profile ID (0-9)</param>
        /// <param name="channel">Channel number (0-3)</param>
        /// <returns>Profile value at specified position</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when profileID or channel is out of range</exception>
        public byte GetProfileValue(int profileID, int channel)
        {
            if (!ValidateProfileID(profileID))
            {
                throw new ArgumentOutOfRangeException(nameof(profileID));
            }
            if (channel < 0 || channel >= ChannelsPerProfile)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }
            return audioProfile[profileID, channel];
        }

        /// <summary>
        /// Set profile value at specified position (safe access with validation)
        /// </summary>
        /// <param name="profileID">Profile ID (0-9)</param>
        /// <param name="channel">Channel number (0-3)</param>
        /// <param name="value">Value to set</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when profileID or channel is out of range</exception>
        public void SetProfileValue(int profileID, int channel, byte value)
        {
            if (!ValidateProfileID(profileID))
            {
                throw new ArgumentOutOfRangeException(nameof(profileID));
            }
            if (channel < 0 || channel >= ChannelsPerProfile)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }
            audioProfile[profileID, channel] = value;
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            // Release any resources here if needed
            // For example: _rasPi?.Dispose();
        }
    }
}