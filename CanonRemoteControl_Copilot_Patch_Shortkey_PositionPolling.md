# GitHub Copilot Patch: CanonRemoteControl Shortkey-App fertigstellen

## Kontext

Copilot hat bereits mehrere Dateien erzeugt, aber die Umsetzung ist noch nicht vollständig:

- `PositionOverlay.xaml.cs` verwendet `PositionPollingService`, diese Klasse existiert jedoch noch nicht.
- `PositionOverlay.xaml.cs` erwartet `CameraPosition` und `CameraStatus`.
- `GlobalKeyboardHook.cs` existiert, wird aber in `App.xaml.cs` noch nicht integriert.
- `App.xaml.cs` startet PTZ aktuell weiterhin über Hotkey-IDs 1 bis 6, aber ohne KeyUp/Stop-Logik.
- `XcCanonPtzController.cs` enthält derzeit wahrscheinlich noch nicht vollständig spec-konforme Parameter für `control.cgi`. Diese Punkte bitte separat prüfen, aber dieser Patch konzentriert sich auf die Shortkey-Anwendung, das fehlende Polling und die KeyDown/KeyUp-Integration.

## Ziel dieses Patches

Die CanonRemoteControl-WPF-Anwendung soll:

1. Im Hintergrund laufen.
2. Presets und Tracking weiter per `RegisterHotKey` unterstützen.
3. PTZ-Bewegung nicht mehr über `RegisterHotKey` steuern.
4. PTZ-Bewegung über `WH_KEYBOARD_LL` und KeyDown/KeyUp steuern.
5. Solange eine PTZ-Taste gedrückt ist, soll die Kamera fahren.
6. Beim Loslassen der Taste soll sofort `StopAllAsync()` ausgeführt werden.
7. `PositionOverlay` soll über `PositionPollingService` alle 250 ms Position und Status abfragen.
8. Das Projekt soll wieder kompilieren.

---

# 1. Fehlende Datei ergänzen: PositionPollingService.cs

## Neue Datei

```text
CanonRemoteControl/PositionPollingService.cs
```

## Inhalt

```csharp
using CanonPtzCommon;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CanonRemoteControl
{
    public sealed class PositionPollingService : IDisposable
    {
        private readonly ICanonPtzController _controller;
        private readonly int _intervalMs;
        private CancellationTokenSource _cts;
        private Task _pollingTask;
        private bool _isRunning;

        public event EventHandler<CameraPosition> PositionUpdated;
        public event EventHandler<CameraStatus> StatusUpdated;

        public PositionPollingService(ICanonPtzController controller, int intervalMs)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _intervalMs = intervalMs <= 0 ? 250 : intervalMs;
        }

        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            try
            {
                _cts?.Cancel();
                _pollingTask?.Wait(1000);
            }
            catch
            {
                // Best effort shutdown.
            }
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    CameraPosition position = await _controller.GetPositionAsync();

                    if (position != null)
                    {
                        PositionUpdated?.Invoke(this, position);
                    }

                    CameraStatus status = await _controller.GetStatusAsync();

                    if (status != null)
                    {
                        StatusUpdated?.Invoke(this, status);
                    }
                }
                catch
                {
                    // Polling darf die Anwendung nicht beenden.
                    // Optional später Logging ergänzen.
                }

                try
                {
                    await Task.Delay(_intervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
            _pollingTask = null;
        }
    }
}
```

---

# 2. Falls nicht vorhanden: CameraPosition.cs ergänzen

## Neue Datei

```text
CanonPtzCommon/CameraPosition.cs
```

## Inhalt

```csharp
namespace CanonPtzCommon
{
    public sealed class CameraPosition
    {
        public int Pan { get; set; }

        public int Tilt { get; set; }

        public int Zoom { get; set; }
    }
}
```

---

# 3. Falls nicht vorhanden: CameraStatus.cs ergänzen

## Neue Datei

```text
CanonPtzCommon/CameraStatus.cs
```

## Inhalt

```csharp
namespace CanonPtzCommon
{
    public sealed class CameraStatus
    {
        public string PanStatus { get; set; }

        public string TiltStatus { get; set; }

        public string ZoomStatus { get; set; }

        public bool IsMoving { get; set; }
    }
}
```

---

# 4. GlobalKeyboardHook.cs korrigieren

## Problem

Die aktuelle Implementierung feuert `KeyUp` nur, wenn Ctrl und Shift beim KeyUp noch gedrückt sind. Wenn der Bediener zuerst Ctrl oder Shift loslässt, bevor die Pfeiltaste losgelassen wird, kommt kein Stop-Event bei der App an.

Für PTZ ist das kritisch, weil die Kamera dann weiterfahren kann.

## Ziel

- `KeyDown` soll nur für Ctrl+Shift+PTZ-Tasten ausgelöst werden.
- `KeyUp` soll für PTZ-Tasten immer ausgelöst werden, auch wenn Ctrl oder Shift bereits losgelassen wurde.
- `WM_SYSKEYDOWN` und `WM_SYSKEYUP` ebenfalls berücksichtigen.
- Modifier-Zustand in EventArgs mitgeben.

## Ersetze GlobalKeyboardHook.cs vollständig durch

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CanonRemoteControl
{
    public sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_SHIFT_LEFT = 0xA0;
        private const int VK_SHIFT_RIGHT = 0xA1;
        private const int VK_CONTROL_LEFT = 0xA2;
        private const int VK_CONTROL_RIGHT = 0xA3;

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event EventHandler<KeyboardHookEventArgs> KeyDown;
        public event EventHandler<KeyboardHookEventArgs> KeyUp;

        public GlobalKeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookId != IntPtr.Zero)
            {
                return;
            }

            _hookId = SetHook(_proc);
        }

        public void Uninstall()
        {
            if (_hookId == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    proc,
                    GetModuleHandle(curModule.ModuleName),
                    0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int message = wParam.ToInt32();

                bool ctrlPressed = IsKeyPressed(VK_CONTROL_LEFT) || IsKeyPressed(VK_CONTROL_RIGHT);
                bool shiftPressed = IsKeyPressed(VK_SHIFT_LEFT) || IsKeyPressed(VK_SHIFT_RIGHT);

                var args = new KeyboardHookEventArgs
                {
                    VirtualKeyCode = vkCode,
                    CtrlPressed = ctrlPressed,
                    ShiftPressed = shiftPressed
                };

                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    KeyDown?.Invoke(this, args);
                }
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    KeyUp?.Invoke(this, args);
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static bool IsKeyPressed(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        public void Dispose()
        {
            Uninstall();
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }

    public sealed class KeyboardHookEventArgs : EventArgs
    {
        public int VirtualKeyCode { get; set; }

        public bool CtrlPressed { get; set; }

        public bool ShiftPressed { get; set; }
    }
}
```

---

# 5. App.xaml.cs patchen

## Problem

Aktuell nutzt `App.xaml.cs` weiterhin `ExecuteHotkeyAsync(id)` für PTZ-IDs 1 bis 6. Dadurch wird nur ein Start-Befehl ausgelöst und kein Stop beim Loslassen.

## Ziel

- `GlobalKeyboardHook` bei GUI-Start installieren.
- PTZ-KeyDown startet Bewegung.
- PTZ-KeyUp ruft `StopAllAsync()` auf.
- Wiederholte KeyDown-Events während gedrückter Taste ignorieren.
- Preset- und Tracking-Hotkeys bleiben über `RegisterHotKey` erhalten.
- `ExecuteHotkeyAsync(id)` darf IDs 1 bis 6 nicht mehr für PTZ verwenden.

## In App.xaml.cs Felder ergänzen

```csharp
private GlobalKeyboardHook _keyboardHook;
private bool _ptzMovementActive;
private int? _activePtzVirtualKey;
```

## Konstanten ergänzen

```csharp
private const int VK_LEFT = 0x25;
private const int VK_UP = 0x26;
private const int VK_RIGHT = 0x27;
private const int VK_DOWN = 0x28;
private const int VK_ESCAPE = 0x1B;
private const int VK_ADD = 0x6B;
private const int VK_SUBTRACT = 0x6D;
private const int VK_OEM_PLUS = 0xBB;
private const int VK_OEM_MINUS = 0xBD;
```

## In StartGuiModeAsync() nach Controller-Connect ergänzen

```csharp
InstallKeyboardHook();
```

Empfohlene Position:

```csharp
private async Task StartGuiModeAsync()
{
    CommandResult connect = await _controller.ConnectAsync();

    _statusOverlay = new StatusOverlay();
    _statusOverlay.Show();

    if (connect.Success)
    {
        _statusOverlay.ShowStatus("BEREIT!\nKamera verbunden", persistent: false);
    }
    else
    {
        _statusOverlay.ShowStatus($"FEHLER:\n{connect.Message}", persistent: true);
    }

    InstallKeyboardHook();

    _mainWindow = new MainWindow();
    _mainWindow.WindowState = WindowState.Minimized;
    _mainWindow.ShowInTaskbar = true;
    _mainWindow.Show();
}
```

## Neue Methoden in App.xaml.cs einfügen

```csharp
private void InstallKeyboardHook()
{
    if (_keyboardHook != null)
    {
        return;
    }

    _keyboardHook = new GlobalKeyboardHook();
    _keyboardHook.KeyDown += KeyboardHook_KeyDown;
    _keyboardHook.KeyUp += KeyboardHook_KeyUp;
    _keyboardHook.Install();
}

private async void KeyboardHook_KeyDown(object sender, KeyboardHookEventArgs e)
{
    if (_controller == null)
    {
        return;
    }

    bool isModifierCombination = e.CtrlPressed && e.ShiftPressed;

    if (!isModifierCombination)
    {
        return;
    }

    if (e.VirtualKeyCode == VK_ESCAPE)
    {
        await StopMovementAsync("EmergencyStop");
        return;
    }

    if (!IsPtzKey(e.VirtualKeyCode))
    {
        return;
    }

    if (_ptzMovementActive)
    {
        return;
    }

    _ptzMovementActive = true;
    _activePtzVirtualKey = e.VirtualKeyCode;

    CommandResult result = await StartMovementForKeyAsync(e.VirtualKeyCode);

    if (result == null)
    {
        return;
    }

    if (result.Success)
    {
        _statusOverlay?.ShowStatus(result.Message, persistent: true);
    }
    else
    {
        _statusOverlay?.ShowStatus($"FEHLER:\n{result.Message}", persistent: false);
        _ptzMovementActive = false;
        _activePtzVirtualKey = null;
    }
}

private async void KeyboardHook_KeyUp(object sender, KeyboardHookEventArgs e)
{
    if (!_ptzMovementActive)
    {
        return;
    }

    if (_activePtzVirtualKey == null)
    {
        return;
    }

    if (e.VirtualKeyCode != _activePtzVirtualKey.Value)
    {
        return;
    }

    await StopMovementAsync("KeyUpStop");
}

private static bool IsPtzKey(int virtualKeyCode)
{
    return virtualKeyCode == VK_LEFT
        || virtualKeyCode == VK_RIGHT
        || virtualKeyCode == VK_UP
        || virtualKeyCode == VK_DOWN
        || virtualKeyCode == VK_ADD
        || virtualKeyCode == VK_SUBTRACT
        || virtualKeyCode == VK_OEM_PLUS
        || virtualKeyCode == VK_OEM_MINUS;
}

private Task<CommandResult> StartMovementForKeyAsync(int virtualKeyCode)
{
    switch (virtualKeyCode)
    {
        case VK_LEFT:
            return _controller.StartPanLeftAsync();

        case VK_RIGHT:
            return _controller.StartPanRightAsync();

        case VK_UP:
            return _controller.StartTiltUpAsync();

        case VK_DOWN:
            return _controller.StartTiltDownAsync();

        case VK_ADD:
        case VK_OEM_PLUS:
            return _controller.StartZoomInAsync();

        case VK_SUBTRACT:
        case VK_OEM_MINUS:
            return _controller.StartZoomOutAsync();

        default:
            return Task.FromResult(CommandResult.Fail("PTZ", $"Nicht unterstützte PTZ-Taste: {virtualKeyCode}"));
    }
}

private async Task StopMovementAsync(string reason)
{
    if (_controller == null)
    {
        _ptzMovementActive = false;
        _activePtzVirtualKey = null;
        return;
    }

    CommandResult result = await _controller.StopAllAsync();

    _ptzMovementActive = false;
    _activePtzVirtualKey = null;

    if (result.Success)
    {
        _statusOverlay?.ShowStatus("PTZ gestoppt", persistent: false);
    }
    else
    {
        _statusOverlay?.ShowStatus($"FEHLER beim Stop:\n{result.Message}", persistent: false);
    }
}
```

## OnExit ergänzen oder überschreiben

Falls noch nicht vorhanden:

```csharp
protected override async void OnExit(ExitEventArgs e)
{
    try
    {
        if (_keyboardHook != null)
        {
            _keyboardHook.KeyDown -= KeyboardHook_KeyDown;
            _keyboardHook.KeyUp -= KeyboardHook_KeyUp;
            _keyboardHook.Dispose();
            _keyboardHook = null;
        }

        if (_controller != null)
        {
            await _controller.StopAllAsync();
            await _controller.DisconnectAsync();
        }
    }
    catch
    {
        // Best effort shutdown.
    }

    base.OnExit(e);
}
```

---

# 6. ExecuteHotkeyAsync in App.xaml.cs anpassen

## Problem

Aktuell sind IDs 1 bis 6 noch PTZ-Bewegung.

## Ziel

PTZ aus `ExecuteHotkeyAsync` entfernen. Presets, Tracking und Hilfe bleiben.

## Ersetze ExecuteHotkeyAsync durch

```csharp
private Task<CommandResult> ExecuteHotkeyAsync(int id)
{
    switch (id)
    {
        // IDs 1 bis 6 waren früher PTZ.
        // PTZ wird jetzt ausschließlich über GlobalKeyboardHook KeyDown/KeyUp gesteuert.
        case 1:
        case 2:
        case 3:
        case 4:
        case 5:
        case 6:
            return Task.FromResult(CommandResult.Ok("PTZ", "PTZ wird über KeyboardHook gesteuert"));

        case 7:
            return _controller.RecallPresetAsync(1);

        case 8:
            return _controller.RecallPresetAsync(2);

        case 9:
            return _controller.RecallPresetAsync(3);

        case 10:
            return _controller.RecallPresetAsync(4);

        case 11:
            return _controller.EnableTrackingSingleAsync();

        case 12:
            return _controller.EnableTrackingGroupAsync();

        case 13:
            return _controller.DisableTrackingAsync();

        case 14:
            _mainWindow?.Dispatcher.Invoke(() =>
            {
                var helpDialog = new HelpDialog
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true
                };
                helpDialog.ShowDialog();
            });
            return Task.FromResult<CommandResult>(null);

        default:
            return Task.FromResult(CommandResult.Fail("Hotkey", $"Unbekannte Hotkey-ID: {id}"));
    }
}
```

---

# 7. MainWindow.xaml.cs anpassen

## Ziel

PTZ-Hotkeys 1 bis 6 nicht mehr per `RegisterHotKey` registrieren.

## Entfernen oder auskommentieren

Alle Registrierungen für:

```text
HOTKEY_ID_UP
HOTKEY_ID_DOWN
HOTKEY_ID_LEFT
HOTKEY_ID_RIGHT
HOTKEY_ID_ZOOM_IN
HOTKEY_ID_ZOOM_OUT
```

## Behalten

```text
HOTKEY_ID_T
HOTKEY_ID_A
HOTKEY_ID_K
HOTKEY_ID_O
HOTKEY_ID_E
HOTKEY_ID_G
HOTKEY_ID_N
HOTKEY_ID_H
```

## Hinweis

Wenn die ID-Nummern 7 bis 14 bleiben, muss nichts in `App.xaml.cs` geändert werden.

Wenn MainWindow die IDs neu nummeriert, dann auch `ExecuteHotkeyAsync` entsprechend anpassen.

---

# 8. PositionOverlay.xaml.cs prüfen

`PositionOverlay.xaml.cs` kann grundsätzlich so bleiben.

Wichtig ist nur, dass diese Controls in `PositionOverlay.xaml` existieren:

```text
PanText
TiltText
ZoomText
StatusText
```

Falls die Namen fehlen, diese in XAML ergänzen oder Code-Behind anpassen.

---

# 9. XcCanonPtzController.cs separat nachziehen

## Achtung

Die aktuelle Datei enthält sehr wahrscheinlich noch nicht korrekte XC-Parameter.

Beispiele aus der aktuellen Datei:

```csharp
SendControlCommandAsync("c.1.pan", "start=-50", "PanLeft")
SendControlCommandAsync("c.1.preset", $"recall={presetNumber}", ...)
SendControlCommandAsync("c.1.tracking", "mode=single", ...)
```

Diese Parameter müssen gegen Appendix iii der Canon XC Control Protocol Specification geprüft und korrigiert werden.

## Separater Copilot Auftrag

```text
Review XcCanonPtzController.cs against Canon XC Control Protocol Specification Appendix iii.
Replace pseudo parameters like:
    c.1.pan&start=-50
    c.1.preset&recall=1
    c.1.tracking&mode=single
with valid control.cgi parameters from the specification.

Also fix session parameter naming:
    use s=<SessionID>
not session=<SessionID>
if the specification requires s.

Parse text/plain responses in the form:
    item:=value
    item==value
not XML.
```

---

# 10. Bereits sichtbare Fehler in XcCanonPtzController.cs

## Fehler 1: Sessionparameter

Aktuell:

```csharp
claim.cgi?session=...
control.cgi?session=...
info.cgi?session=...
```

Soll laut Spec wahrscheinlich sein:

```text
s=<SessionID>
```

Also:

```csharp
claim.cgi?s=...
control.cgi?s=...
info.cgi?s=...
```

## Fehler 2: Response Parsing

Aktuell wird XML erwartet:

```csharp
XDocument.Parse(responseBody)
```

Die Spec liefert aber text/plain Key-Value-Zeilen:

```text
s:=8a96-c09b18f0
c.1.pan:=1234
c.1.tilt:=567
```

Daher braucht es einen Textparser.

## Fehler 3: URL Aufbau

Aktuell:

```csharp
string url = $"{_baseUrl}/{path}";
```

Fehlend ist:

```text
/-wvhttp-01-/
```

Korrekt:

```csharp
string url = $"{_baseUrl}/-wvhttp-01-/{path}";
```

## Fehler 4: Control Command Aufbau

Aktuell:

```csharp
control.cgi?session=...&c.1.pan&start=-50
```

Das ist sehr wahrscheinlich ungültig.

Benötigt wird eine gültige Query im Format:

```text
control.cgi?s=<session>&<Parameter>=<Value>
```

## Fehler 5: Livescope-Status wird nicht geprüft

`CommandResult` sollte neben HTTP-Status auch den Header `livescope-status` auswerten.

---

# 11. Korrektur für SendCgiRequestAsync

## Ersetze URL-Aufbau

```csharp
string url = $"{_baseUrl}/-wvhttp-01-/{path}";
```

## Livescope-Status lesen

```csharp
private static int? GetLivescopeStatus(HttpResponseMessage response)
{
    if (response.Headers.TryGetValues("livescope-status", out var values))
    {
        foreach (string value in values)
        {
            if (int.TryParse(value, out int status))
            {
                return status;
            }
        }
    }

    return null;
}
```

## Bewertung

```csharp
int? livescopeStatus = GetLivescopeStatus(response);

if (response.IsSuccessStatusCode && (!livescopeStatus.HasValue || livescopeStatus.Value == 0))
{
    return CommandResult.Ok(commandName, "OK", response.StatusCode, body);
}

return CommandResult.Fail(
    commandName,
    $"HTTP/Livescope Fehler: HTTP {(int)response.StatusCode}, Livescope {livescopeStatus}",
    response.StatusCode,
    body);
```

---

# 12. Textparser für XC Responses ergänzen

## Neue Hilfsmethode

```csharp
private static Dictionary<string, string> ParseXcKeyValueResponse(string body)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(body))
    {
        return result;
    }

    string[] lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    foreach (string rawLine in lines)
    {
        string line = rawLine.Trim();

        int index = line.IndexOf(":=", StringComparison.Ordinal);
        int separatorLength = 2;

        if (index < 0)
        {
            index = line.IndexOf("==", StringComparison.Ordinal);
            separatorLength = 2;
        }

        if (index <= 0)
        {
            continue;
        }

        string key = line.Substring(0, index).Trim();
        string value = line.Substring(index + separatorLength).Trim();

        result[key] = value;
    }

    return result;
}
```

## ExtractSessionId ersetzen

```csharp
private string ExtractSessionId(string responseBody)
{
    Dictionary<string, string> values = ParseXcKeyValueResponse(responseBody);

    if (values.TryGetValue("s", out string sessionId))
    {
        return sessionId;
    }

    return string.Empty;
}
```

## ParsePosition ersetzen

```csharp
private CameraPosition ParsePosition(string responseBody)
{
    Dictionary<string, string> values = ParseXcKeyValueResponse(responseBody);

    return new CameraPosition
    {
        Pan = TryGetInt(values, "c.1.pan"),
        Tilt = TryGetInt(values, "c.1.tilt"),
        Zoom = TryGetInt(values, "c.1.zoom")
    };
}
```

## ParseStatus ersetzen

```csharp
private CameraStatus ParseStatus(string responseBody)
{
    Dictionary<string, string> values = ParseXcKeyValueResponse(responseBody);

    string panStatus = TryGetString(values, "c.1.pan.status", "0");
    string tiltStatus = TryGetString(values, "c.1.tilt.status", "0");
    string zoomStatus = TryGetString(values, "c.1.zoom.status", "0");

    return new CameraStatus
    {
        PanStatus = panStatus,
        TiltStatus = tiltStatus,
        ZoomStatus = zoomStatus,
        IsMoving = panStatus != "0" || tiltStatus != "0" || zoomStatus != "0"
    };
}
```

## Helper ergänzen

```csharp
private static int TryGetInt(Dictionary<string, string> values, string key)
{
    if (values.TryGetValue(key, out string value) && int.TryParse(value, out int parsed))
    {
        return parsed;
    }

    return 0;
}

private static string TryGetString(Dictionary<string, string> values, string key, string fallback)
{
    if (values.TryGetValue(key, out string value))
    {
        return value;
    }

    return fallback;
}
```

---

# 13. Copilot Gesamtauftrag

```text
Apply the CanonRemoteControl shortkey patch.

Add missing PositionPollingService.cs.

Add CameraPosition.cs and CameraStatus.cs if missing.

Replace GlobalKeyboardHook.cs with a version that always raises KeyUp for PTZ keys even if Ctrl or Shift was released first.

Integrate GlobalKeyboardHook into App.xaml.cs.

Implement:
    Ctrl+Shift+Left  KeyDown -> StartPanLeftAsync
    Ctrl+Shift+Right KeyDown -> StartPanRightAsync
    Ctrl+Shift+Up    KeyDown -> StartTiltUpAsync
    Ctrl+Shift+Down  KeyDown -> StartTiltDownAsync
    Ctrl+Shift+Plus  KeyDown -> StartZoomInAsync
    Ctrl+Shift+Minus KeyDown -> StartZoomOutAsync

On corresponding KeyUp call StopAllAsync.

Add Ctrl+Shift+Escape as emergency stop.

Remove PTZ movement from RegisterHotKey handling.

Keep RegisterHotKey only for presets, tracking and help.

Ensure PositionOverlay can poll position and status using PositionPollingService.

Then fix XcCanonPtzController infrastructure:
    Add /-wvhttp-01-/ to every request URL.
    Use s=<SessionID> instead of session=<SessionID>.
    Parse text/plain XC responses with := or == separators.
    Do not use XML parsing for XC responses.
    Evaluate livescope-status header.

Leave actual PTZ parameter mapping as TODO if Appendix iii parameter names are not yet verified.
```

---

# 14. Akzeptanzkriterien

## Build

- Projekt kompiliert.
- Keine fehlende Klasse `PositionPollingService` mehr.
- Keine fehlenden Typen `CameraPosition` oder `CameraStatus` mehr.

## Shortkey Verhalten

- `Ctrl+Shift+Left` gedrückt: Kamera startet Bewegung links.
- `Ctrl+Shift+Left` losgelassen: `StopAllAsync()` wird ausgeführt.
- Gleiches Verhalten für rechts, hoch, runter, Zoom In, Zoom Out.
- `Ctrl+Shift+Escape` stoppt sofort alle Bewegungen.

## RegisterHotKey

- Presets funktionieren weiter.
- Tracking funktioniert weiter.
- Hilfe funktioniert weiter.
- PTZ ist nicht mehr über RegisterHotKey aktiv.

## Overlay

- `PositionOverlay` startet ohne Compilefehler.
- Position wird über Events aktualisiert.
- Status wird über Events aktualisiert.

## XC Infrastruktur

- URLs verwenden `/-wvhttp-01-/`.
- Sessionparameter heißt `s`.
- Responses werden als Text geparst, nicht als XML.
- Livescope-Status wird geprüft.
```
