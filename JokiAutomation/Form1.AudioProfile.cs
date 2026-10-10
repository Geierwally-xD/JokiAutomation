using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JokiAutomation
{
    public partial class Form1
    {
        private void LogAudioProfileListState(string requestedProfile)
        {
            int selectedIndex = listBox4?.SelectedIndex ?? -1;
            string selectedItem = listBox4?.SelectedItem?.ToString() ?? "<null>";
            int itemCount = listBox4?.Items.Count ?? 0;

            _logDat?.sendInfoMessage(
                $"JokiAutomation\n[AUDIO_PROFILE_LIST] Requested='{requestedProfile}' Count={itemCount} SelectedIndex={selectedIndex} SelectedItem='{selectedItem}' RequestId='{_activeCommandRequestId.Value}'");

            if (listBox4 == null)
            {
                return;
            }

            for (int i = 0; i < listBox4.Items.Count; i++)
            {
                string item = listBox4.Items[i]?.ToString() ?? "<null>";
                _logDat?.sendInfoMessage($"JokiAutomation\n[AUDIO_PROFILE_ITEM] Index={i} Name='{item}' RequestId='{_activeCommandRequestId.Value}'");
            }
        }

        private async Task<bool> ApplyAudioProfileByNameAsync(
            string profileName,
            TimeSpan? postSendDelay = null,
            CancellationToken cancellationToken = default,
            bool waitForCompletion = true)
        {
            _logDat?.sendInfoMessage($"[AUDIO] PROFILE_BEGIN Profile=\"{profileName}\" Source=\"CLI_NAME\"");
            LogAudioProfileListState(profileName);

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
                if (!string.Equals(itemText, profileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (profileIndex >= 0)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nMehrdeutiges Tonprofil '{profileName}' in Profilliste erkannt.");
                    return false;
                }

                profileIndex = i;
            }

            if (profileIndex < 0)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nTonprofil '{profileName}' nicht gefunden.");
                return false;
            }

            return await SendAudioProfileAsync(profileIndex, "CLI_NAME", profileName, postSendDelay, cancellationToken, waitForCompletion);
        }

        private async Task<bool> ApplyAudioProfileByIndexAsync(
            int profileIndex,
            TimeSpan? postSendDelay = null,
            CancellationToken cancellationToken = default)
        {
            string requestedName = listBox4 != null && profileIndex >= 0 && profileIndex < listBox4.Items.Count
                ? (listBox4.Items[profileIndex]?.ToString() ?? "<null>")
                : "<invalid>";

            _logDat?.sendInfoMessage($"[AUDIO] PROFILE_BEGIN Profile=\"{requestedName}\" Source=\"GUI_INDEX\"");
            LogAudioProfileListState(requestedName);

            return await SendAudioProfileAsync(profileIndex, "GUI_INDEX", requestedName, postSendDelay, cancellationToken, waitForCompletion: true);
        }

        private async Task<bool> SendAudioProfileAsync(
            int profileIndex,
            string source,
            string requestedName,
            TimeSpan? postSendDelay = null,
            CancellationToken cancellationToken = default,
            bool waitForCompletion = true)
        {
            TimeSpan effectiveDelay = postSendDelay ?? TimeSpan.FromMilliseconds(500);

            try
            {
                if (listBox4 == null || listBox4.Items.Count == 0)
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nTonprofil-Liste ist nicht verfügbar.");
                    return false;
                }

                if (profileIndex < 0 || profileIndex >= listBox4.Items.Count)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nUngültiger Tonprofil-Index: {profileIndex}");
                    return false;
                }

                listBox4.SelectedIndex = profileIndex;

                if (_audioMix?.audioProfile != null)
                {
                    trackBar1.Value = _audioMix.audioProfile[profileIndex, 0];
                    trackBar2.Value = _audioMix.audioProfile[profileIndex, 1];
                    trackBar3.Value = _audioMix.audioProfile[profileIndex, 2];
                    trackBar4.Value = _audioMix.audioProfile[profileIndex, 3];
                }

                if (_audioMix == null)
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nAudioMix ist nicht initialisiert.");
                    return false;
                }

                int commandID = AudioMix.AM_PROFILE + profileIndex;
                _logDat?.sendInfoMessage(
                    $"JokiAutomation\n[AUDIO_PROFILE_RESOLVED] Requested='{requestedName}' Source='{source}' ProfileIndex={profileIndex} CommandID={commandID} RequestId='{_activeCommandRequestId.Value}'");
                _logDat?.sendInfoMessage(
                    $"JokiAutomation\n[AUDIO_DISPATCH] Command=30 ID={commandID} RequestId='{_activeCommandRequestId.Value}' WaitForCompletion={waitForCompletion}");

                if (waitForCompletion)
                {
                    await _audioMix.ExecuteAudioAsync(commandID, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(effectiveDelay, cancellationToken);

                    _logDat?.sendInfoMessage($"JokiAutomation\nTonprofil auf {requestedName} gesetzt");
                    _logDat?.sendInfoMessage($"[AUDIO] PROFILE_SUCCESS Profile=\"{requestedName}\" Source=\"{source}\"");
                    _logDat?.sendInfoMessage($"[AUDIO_PROFILE_COMPLETED] Profile=\"{requestedName}\" Source=\"{source}\" Result=\"Success\"");
                    Debug.WriteLine($"SendAudioProfileAsync END: index={profileIndex}, profile='{requestedName}', source='{source}', mode=awaited");
                    return true;
                }

                DateTime queuedAtUtc = DateTime.UtcNow;
                QueueAudioProfileDispatch(commandID, requestedName, source, effectiveDelay, _activeCommandRequestId.Value, queuedAtUtc);
                _logDat?.sendInfoMessage(
                    $"JokiAutomation\n[AUDIO_DISPATCH_QUEUED] Command=30 ID={commandID} RequestId='{_activeCommandRequestId.Value}' QueuedAtUtc='{queuedAtUtc:O}'");
                _logDat?.sendInfoMessage(
                    $"JokiAutomation\n[PIPE_PARALLEL] AudioQueued RequestId='{_activeCommandRequestId.Value}' Profile='{requestedName}' CommandId={commandID} VisualMayProceed=True");
                return true;
            }
            catch (OperationCanceledException)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nTonprofil '{requestedName}' abgebrochen.");
                _logDat?.sendInfoMessage($"[AUDIO_PROFILE_COMPLETED] Profile=\"{requestedName}\" Source=\"{source}\" Result=\"Canceled\"");
                throw;
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nTonprofil-Fehler: {ex.Message}");
                _logDat?.sendInfoMessage($"[AUDIO] PROFILE_FAILED Profile=\"{requestedName}\" Source=\"{source}\" Error=\"{ex.Message}\"");
                _logDat?.sendInfoMessage($"[AUDIO_PROFILE_COMPLETED] Profile=\"{requestedName}\" Source=\"{source}\" Result=\"Failed\"");
                throw;
            }
        }

        private Task<bool> ApplyAudioProfileAsync(
            string profileName,
            TimeSpan? postSendDelay = null,
            CancellationToken cancellationToken = default)
        {
            return ApplyAudioProfileByNameAsync(profileName, postSendDelay, cancellationToken);
        }

        private Task<bool> QueueAudioProfileByNameAsync(
            string profileName,
            TimeSpan? postSendDelay = null,
            CancellationToken cancellationToken = default)
        {
            return ApplyAudioProfileByNameAsync(profileName, postSendDelay, cancellationToken, waitForCompletion: false);
        }

        private void QueueAudioProfileDispatch(
            int commandID,
            string requestedName,
            string source,
            TimeSpan postSendDelay,
            string requestId,
            DateTime queuedAtUtc)
        {
            lock (_audioPipelineSync)
            {
                _audioPipelineTail = _audioPipelineTail.ContinueWith(async _ =>
                {
                    try
                    {
                        _logDat?.sendInfoMessage(
                            $"JokiAutomation\n[PIPE_PARALLEL] AudioBackgroundStart RequestId='{requestId}' Profile='{requestedName}' CommandId={commandID} DelaySinceQueueMs={(int)(DateTime.UtcNow - queuedAtUtc).TotalMilliseconds}");

                        await _audioMix.ExecuteAudioAsync(commandID, CancellationToken.None);
                        await Task.Delay(postSendDelay, CancellationToken.None);
                        _logDat?.sendInfoMessage(
                            $"JokiAutomation\n[AUDIO_PROFILE_COMPLETED] Profile=\"{requestedName}\" Source=\"{source}\" RequestId='{requestId}' Result=\"SuccessBackground\"");
                        _logDat?.sendInfoMessage(
                            $"JokiAutomation\n[PIPE_PARALLEL] AudioBackgroundCompleted RequestId='{requestId}' Profile='{requestedName}' CommandId={commandID}");
                    }
                    catch (Exception ex)
                    {
                        _logDat?.sendInfoMessage(
                            $"JokiAutomation\n[AUDIO] PROFILE_FAILED Profile=\"{requestedName}\" Source=\"{source}\" RequestId='{requestId}' Error=\"{ex.Message}\"");
                        _logDat?.sendInfoMessage(
                            $"JokiAutomation\n[AUDIO_PROFILE_COMPLETED] Profile=\"{requestedName}\" Source=\"{source}\" RequestId='{requestId}' Result=\"FailedBackground\"");
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            }
        }
    }
}
