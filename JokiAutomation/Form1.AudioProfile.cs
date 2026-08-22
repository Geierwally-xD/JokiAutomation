using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JokiAutomation
{
    public partial class Form1
    {
        /// <summary>
        /// Applies an audio profile the same way the Audio menu start button does:
        /// selects the profile, updates the UI, and sends the Raspberry Pi command.
        /// </summary>
        private async Task<bool> ApplyAudioProfileAsync(
            string profileName,
            TimeSpan? postSendDelay = null,
            CancellationToken cancellationToken = default)
        {
            TimeSpan effectiveDelay = postSendDelay ?? TimeSpan.FromMilliseconds(500);

            try
            {
                Debug.WriteLine($"ApplyAudioProfileAsync START: requested='{profileName}'");

                if (string.IsNullOrWhiteSpace(profileName))
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nTonprofil ist leer.");
                    return false;
                }

                if (listBox4 == null || listBox4.Items.Count == 0)
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nTonprofil-Liste ist nicht verfügbar.");
                    return false;
                }

                int profileIndex = -1;
                for (int i = 0; i < listBox4.Items.Count; i++)
                {
                    string itemText = listBox4.Items[i]?.ToString();
                    Debug.WriteLine($"ApplyAudioProfileAsync: item[{i}] = '{itemText}'");

                    if (string.Equals(itemText, profileName, StringComparison.OrdinalIgnoreCase))
                    {
                        profileIndex = i;
                        break;
                    }
                }

                if (profileIndex < 0)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nTonprofil '{profileName}' nicht gefunden.");
                    Debug.WriteLine($"ApplyAudioProfileAsync ABORT: profile '{profileName}' not found");
                    return false;
                }

                listBox4.SelectedIndex = profileIndex;
                Debug.WriteLine($"ApplyAudioProfileAsync: SelectedIndex = {profileIndex}");

                if (_audioMix?.audioProfile != null)
                {
                    trackBar1.Value = _audioMix.audioProfile[profileIndex, 0];
                    trackBar2.Value = _audioMix.audioProfile[profileIndex, 1];
                    trackBar3.Value = _audioMix.audioProfile[profileIndex, 2];
                    trackBar4.Value = _audioMix.audioProfile[profileIndex, 3];

                    Debug.WriteLine($"ApplyAudioProfileAsync: trackbars = {trackBar1.Value}, {trackBar2.Value}, {trackBar3.Value}, {trackBar4.Value}");
                }

                int commandID = AudioMix.AM_PROFILE + profileIndex;
                Debug.WriteLine($"ApplyAudioProfileAsync: sending executeAudio({commandID})");
                _audioMix?.executeAudio(commandID);

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(effectiveDelay, cancellationToken);

                _logDat?.sendInfoMessage($"JokiAutomation\nTonprofil auf {profileName} gesetzt");
                Debug.WriteLine($"ApplyAudioProfileAsync END: '{profileName}'");
                return true;
            }
            catch (OperationCanceledException)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nTonprofil '{profileName}' abgebrochen.");
                Debug.WriteLine($"ApplyAudioProfileAsync CANCELED: '{profileName}'");
                throw;
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nTonprofil-Fehler: {ex.Message}");
                Debug.WriteLine($"ApplyAudioProfileAsync ERROR: {ex.GetType().Name} - {ex.Message}");
                throw;
            }
        }
    }
}
