# Umsetzungsreihenfolge
# Canon CR-N100 XC Protocol Integration
# Refactoring Master Specification
## JoKi Automation + CanonRemoteControl + Canon XC Protocol

Version: 2.0 (XC Protocol angepasst)

---

# Ziel

Die Canon CR-N100 soll vollständig über das Canon XC Protocol in die bestehende Automatisierungsumgebung integriert werden.

Die Lösung besteht aus:

1. JoKi Automation (WinForms)
2. CanonRemoteControl (WPF)
3. Shared Library CanonPtzCommon
4. Canon XC Protocol Controller

---

# Hauptziele

## PTZ

- Pan
- Tilt
- Zoom

über XC Protocol

---

## Presets

- Taufstein
- Altar
- Kanzel
- Orgel
- weitere Presets

---

## Tracking

- Einzelperson
- Gruppe
- Tracking Aus

---

## Automatisierung

Integration in:

```text
CommandInterpreter

PositionControl

AutoZoom

ATEM

RasPi
```

---

## Bedienung

Die Kamera soll im Hintergrund steuerbar sein.

### Verhalten

```text
Taste gedrückt
    →
Kamera fährt

Taste losgelassen
    →
Kamera stoppt
```

### Anwendung

```text
PPP

PowerPoint

OBS

Browser

Teams
```

dürfen im Vordergrund bleiben.

---

# Zielarchitektur

```text
                 +-----------------+
                 |   Network.cfg   |
                 +--------+--------+
                          |
                          v

               +----------------------+
               |    CanonPtzCommon    |
               +----------------------+

                 ICanonPtzController

                          |
        +-----------------+-----------------+
        |                                   |

        v                                   v

LegacyAwPtzController         XcCanonPtzController
(Fallback)                    (Produktiv)

                          |
                          v

                   Canon CR-N100
```

---

# Canon XC Protocol

## Verwendete CGI Endpunkte

### Session

```text
open.cgi

claim.cgi

session.cgi

yield.cgi

close.cgi
```

### Steuerung

```text
control.cgi
```

### Status

```text
info.cgi
```

---

# Kommunikationsmodell

## Programmstart

```text
open.cgi

claim.cgi
```

Ziel:

```text
Session erzeugen

Steuerrecht anfordern
```

---

## Normalbetrieb

### PTZ

```text
control.cgi
```

### Status

```text
info.cgi
```

---

## Programmende

```text
yield.cgi

close.cgi
```

---

# Neue Bibliothek

## CanonPtzCommon

```text
CanonPtzCommon

 ├─ ICanonPtzController.cs
 ├─ CommandResult.cs
 ├─ CameraConfig.cs
 ├─ NetworkCfgReader.cs
 ├─ CameraCommand.cs
 ├─ CameraCommandService.cs
 ├─ CameraPosition.cs
 ├─ CameraStatus.cs
 ├─ CameraControlState.cs
 └─ PositionPollingService.cs
```

---

# CommandResult

Ersetzt bool.

## Enthält

```csharp
Success

Message

HttpStatusCode

ResponseBody

LivescopeStatus

Exception

Command
```

---

# CameraConfig

## Quelle

```text
Network.cfg
```

---

## Beispiel

```text
Canon_CRN100;192.168.178.120;443;admin;Passwort
```

---

# ICanonPtzController

## Verbindung

```csharp
Task<CommandResult> ConnectAsync();

Task<CommandResult> DisconnectAsync();
```

---

## Presets

```csharp
Task<CommandResult> RecallPresetAsync(int preset);

Task<CommandResult> StorePresetAsync(int preset);
```

---

## Tracking

```csharp
Task<CommandResult> EnableTrackingSingleAsync();

Task<CommandResult> EnableTrackingGroupAsync();

Task<CommandResult> DisableTrackingAsync();
```

---

## PTZ

```csharp
Task<CommandResult> StartPanLeftAsync();

Task<CommandResult> StartPanRightAsync();

Task<CommandResult> StartTiltUpAsync();

Task<CommandResult> StartTiltDownAsync();

Task<CommandResult> StartZoomInAsync();

Task<CommandResult> StartZoomOutAsync();

Task<CommandResult> StopPanAsync();

Task<CommandResult> StopTiltAsync();

Task<CommandResult> StopZoomAsync();

Task<CommandResult> StopAllAsync();
```

---

## Status

```csharp
Task<CameraPosition> GetPositionAsync();

Task<CameraStatus> GetStatusAsync();
```

---

# LegacyAwPtzController

## Umbenennen

Von

```text
CanonCrn100Controller
```

nach

```text
LegacyAwPtzController
```

---

## Zweck

Nur Fallback.

Nicht mehr Standard.

---

# XcCanonPtzController

## Standardimplementierung

Neue Produktivimplementierung.

Verwendet ausschließlich:

```text
control.cgi

info.cgi

open.cgi

claim.cgi

yield.cgi

close.cgi
```

---

# Session Lifecycle

## ConnectAsync()

```text
open.cgi

claim.cgi
```

---

## DisconnectAsync()

```text
yield.cgi

close.cgi
```

---

# PTZ Steuerung

## Neues Konzept

Nicht mehr:

```text
RegisterHotKey
       ↓
Einzelbefehl
```

---

Sondern:

```text
KeyDown
      ↓
Start Bewegung

KeyUp
      ↓
Stop Bewegung
```

---

# Hintergrundsteuerung

## Neue Klasse

```text
GlobalKeyboardHook.cs
```

Technologie:

```text
WH_KEYBOARD_LL
```

---

# Presets bleiben RegisterHotKey

Weiterhin:

```text
Ctrl+Shift+A

Ctrl+Shift+T

Ctrl+Shift+K

Ctrl+Shift+O
```

---

Tracking:

```text
Ctrl+Shift+E

Ctrl+Shift+G

Ctrl+Shift+N
```

---

# PTZ Tastenkombinationen

```text
Ctrl+Shift+Left

Ctrl+Shift+Right

Ctrl+Shift+Up

Ctrl+Shift+Down

Ctrl+Shift++

Ctrl+Shift+-
```

---

# KeyDown Verhalten

## Links

```text
StartPanLeftAsync()
```

## Rechts

```text
StartPanRightAsync()
```

## Hoch

```text
StartTiltUpAsync()
```

## Runter

```text
StartTiltDownAsync()
```

## Zoom In

```text
StartZoomInAsync()
```

## Zoom Out

```text
StartZoomOutAsync()
```

---

# KeyUp Verhalten

Bei Loslassen:

```csharp
StopAllAsync();
```

---

# Warum StopAll?

Einfachste und sicherste Lösung:

```text
Pan stoppen

Tilt stoppen

Zoom stoppen
```

in einem Aufruf.

---

# Notfallstop

## Hotkey

```text
Ctrl+Shift+Escape
```

## Aktion

```csharp
StopAllAsync();
```

---

# Kein PtzRealtimeControlService mehr

Die erste Entwurfsversion verwendete:

```text
50 ms Timer

Permanent Requests senden
```

Das soll nicht mehr die Hauptlösung sein.

---

# Neue Strategie

```text
Start Bewegung

↓

Kamera bewegt sich

↓

Stop Bewegung
```

Der Timer bleibt optional als Fallback.

---

# Position Polling

## Neue Klasse

```text
PositionPollingService.cs
```

---

# Aktualisierung

```text
250 ms
```

---

# Datenquelle

```text
info.cgi
```

---

# Abfragen

```text
c.1.pan

c.1.tilt

c.1.zoom
```

---

# Status

```text
c.1.pan.status

c.1.tilt.status

c.1.zoom.status
```

---

# CameraPosition

```csharp
public class CameraPosition
{
    public int Pan { get; set; }

    public int Tilt { get; set; }

    public int Zoom { get; set; }
}
```

---

# CameraStatus

```csharp
public class CameraStatus
{
    public string PanStatus { get; set; }

    public string TiltStatus { get; set; }

    public string ZoomStatus { get; set; }

    public bool IsMoving { get; set; }
}
```

---

# PositionOverlay

## Neue Dateien

```text
PositionOverlay.xaml

PositionOverlay.xaml.cs
```

---

# Anzeige

```text
PAN  : 4210

TILT : 1350

ZOOM : 5120

STATUS : MOVING
```

---

# Overlay Eigenschaften

```text
TopMost

Transparent

ClickThrough

Nicht fokussierbar
```

---

# Overlay Hotkey

```text
Ctrl+Shift+P
```

---

# Funktion

```text
Overlay EIN

Overlay AUS
```

---

# Tray Betrieb

## Ziel

Programm läuft im Hintergrund.

---

## Beim Start

```text
MainWindow verstecken
```

---

## Sichtbar

```text
TrayIcon
```

---

# Tray Menü

```text
Canon Remote Control

Verbindung prüfen

Overlay ein/aus

Info

Beenden
```

---

# App.xaml.cs

## Entfernen

```csharp
async void RunCommandLineMode()
```

---

## Neu

```csharp
Task<int> RunCommandLineModeAsync()
```

---

# Kommandozeilenbefehle

Bestehend:

```text
altar

taufstein

kanzel

orgel

track_single

track_group

track_off
```

---

Neu:

```text
pan_left

pan_right

tilt_up

tilt_down

zoom_in

zoom_out

stop
```

---

# MainWindow

## Problem

Fenster wird versteckt

aber Hotkeys entfernt.

---

## Lösung

Fenster schließen:

```text
Hide()
```

Keine Deregistrierung.

---

## Beenden

```text
ExitApplication()
```

führt aus:

```text
Hotkeys entfernen

Hooks entfernen

Shutdown
```

---

# StatusOverlay

## Start

```text
unsichtbar
```

---

## Kein

```csharp
Activate()
```

mehr.

---

## Farben

```text
Grün

Orange

Rot
```

---

# Logging

## PTZ

```text
PAN LEFT START

PAN RIGHT START

TILT UP START

TILT DOWN START

ZOOM IN START

ZOOM OUT START

STOP ALL
```

---

## Presets

```text
PRESET ALTAR

PRESET KANZEL

PRESET ORGEL

PRESET TAUFSTEIN
```

---

## Tracking

```text
TRACK SINGLE

TRACK GROUP

TRACK OFF
```

---

# JoKi Automation

## Neues Feld

```csharp
private ICanonPtzController _canonPtz;
```

---

# Initialisierung

Nach:

```csharp
InitializeNetworkConfig();

InitializeATEMControl();
```

aufrufen:

```csharp
await InitializeCanonPtzControlAsync();
```

---

# InitializeCanonPtzControlAsync

Aufgaben:

```text
Network.cfg lesen

Controller erzeugen

Session öffnen

Control Claim

Logging
```

---

# ExecuteCanonSceneAsync

Neue Methode.

---

Unterstützte Szenen:

```text
Altar

Predigt

Taufstein

Orgel
```

---

# Ablauf Szene

```text
Preset Recall

Warten bis Position erreicht

ATEM umschalten

PiP deaktivieren

Status loggen
```

---

# CommandInterpreter

Migration:

Von:

```csharp
void
```

Nach:

```csharp
async Task
```

---

# ATEM

## Alte Namen

```text
CamcorderMain

CamcorderPreacher
```

---

## Neue Namen

```text
CanonPtzMain

CanonPtzPreacher
```

---

# Sicherheit

## Entfernen

```text
Hardcoded Passwort
```

---

## Verwenden

```text
Network.cfg

oder

Windows Credential Manager
```

---

# Fehlerbehandlung

## Timeout

```text
3 Sekunden
```

---

## Reconnect

```text
3 Versuche
```

---

## Danach

```csharp
DisconnectAsync();

ConnectAsync();
```

---

## Notfall

```csharp
StopAllAsync();
```

---

# Testplan

## CanonRemoteControl

### Start

- GUI
- Tray
- Kommandozeile

---

### PTZ

Taste drücken:

```text
Links

Rechts

Hoch

Runter

Zoom
```

---

Taste loslassen:

```text
Stop
```

---

### Overlay

```text
PAN

TILT

ZOOM

STATUS
```

korrekt.

---

### Tracking

```text
Single

Group

Off
```

funktioniert.

---

## JoKi Automation

### Ohne Kamera

Programm läuft weiter.

---

### Mit Kamera

Verbindung erfolgreich.

---

### Szenen

```text
Altar

Predigt

Taufstein

Orgel
```

korrekt.

---

### ATEM

Richtige Umschaltung.

---

# GitHub Copilot Master Task

```text
Refactor JoKi Automation and CanonRemoteControl for native Canon XC Protocol support.

Create a shared CanonPtzCommon library.

Implement XcCanonPtzController using:

open.cgi
claim.cgi
control.cgi
info.cgi
yield.cgi
close.cgi

Maintain session ownership and control rights.

Use WH_KEYBOARD_LL for PTZ movement.

Implement:

KeyDown -> Start movement

KeyUp -> StopAllAsync

Keep presets and tracking commands on RegisterHotKey.

Create PositionPollingService using info.cgi.

Create PositionOverlay showing:

Pan
Tilt
Zoom
Movement Status

Run as tray application without visible main window.

Integrate Canon control into JoKi Automation.

Support scene execution, ATEM switching, preset recall and tracking.

Keep LegacyAwPtzController only as fallback.
```

# Umsetzungsreihenfolge

## Phase 1

- CanonPtzCommon
- CommandResult
- NetworkCfgReader
- CameraConfig
- ICanonPtzController

## Phase 2

- XcCanonPtzController
- Session Handling
- control.cgi
- info.cgi

## Phase 3

- GlobalKeyboardHook
- StopAllAsync
- Tray Betrieb

## Phase 4

- PositionPollingService
- PositionOverlay

## Phase 5

- JoKi Automation Integration
- ExecuteCanonSceneAsync
- ATEM Mapping

## Phase 6

- Reconnect
- Diagnose
- Endabnahme