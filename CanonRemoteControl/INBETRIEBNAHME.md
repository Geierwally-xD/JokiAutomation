# Canon CRN-100 Remote Control - Inbetriebnahme

## Voraussetzungen

- Canon CRN-100 PTZ-Kamera im Netzwerk erreichbar
- Windows-PC mit .NET Framework 4.8
- PowerPoint (optional, für Präsentationsmodus)

## Installation

1. **Kompilieren Sie das Projekt** in Visual Studio oder führen Sie die fertige .exe aus
2. **Konfigurationsdatei erstellen:**
   - Kopieren Sie `Canon.cfg.template` nach `Canon.cfg` im Ausgabeverzeichnis (bin\Debug oder bin\Release)
   - Passen Sie die IP-Adresse der Kamera an
   - Optional: Benutzername und Passwort eintragen

### Beispiel Canon.cfg

```
# Canon CRN-100 Konfiguration
CANON_IP=192.168.1.100
CANON_USER=admin
CANON_PASSWORD=
```

## Preset-Konfiguration auf der Kamera

Bevor Sie die Applikation verwenden, müssen auf der Canon CRN-100 folgende Presets eingerichtet werden:

1. **Preset 1: Taufsteinposition**
   - Kamera manuell zur Taufsteinposition fahren
   - Auf der Kamera oder im Webinterface: Preset 1 speichern

2. **Preset 2: Altarposition**
   - Kamera zur Altarposition fahren
   - Preset 2 speichern

3. **Preset 3: Kanzelposition**
   - Kamera zur Kanzelposition fahren
   - Preset 3 speichern

4. **Preset 4: Orgelposition**
   - Kamera zur Orgelposition fahren
   - Preset 4 speichern

### Presets über Webinterface speichern

1. Öffnen Sie `http://[KAMERA-IP]` im Browser
2. Loggen Sie sich ein (Standard: admin / kein Passwort)
3. Navigieren Sie zur PTZ-Steuerung
4. Fahren Sie die gewünschte Position an
5. Klicken Sie auf "Set" neben der Preset-Nummer
6. Wiederholen Sie dies für alle vier Positionen

## Erste Inbetriebnahme

1. **Starten Sie die Applikation** `CanonRemoteControl.exe`
2. Das Hauptfenster erscheint kurz und minimiert sich dann automatisch
3. Testen Sie die Preset-Positionen:
   - Drücken Sie `Ctrl+Alt+T` für Taufstein
   - Drücken Sie `Ctrl+Alt+A` für Altar
   - Drücken Sie `Ctrl+Alt+K` für Kanzel
   - Drücken Sie `Ctrl+Alt+O` für Orgel
4. Eine Statusmeldung erscheint oben rechts im Bildschirm

## Verwendung während PowerPoint-Präsentation

1. Starten Sie die `CanonRemoteControl.exe` Applikation
2. Öffnen Sie PowerPoint und starten Sie die Präsentation (F5)
3. Die Tastaturkürzel funktionieren während der Präsentation
4. Statusmeldungen werden über der Präsentation eingeblendet

### Typischer Ablauf während Gottesdienst

```
Ctrl+Alt+E    ? Live-Tracking Einzelperson aktivieren (für Prediger)
              ? Status bleibt sichtbar: "Live-Tracking Einzelperson aktiv"

Ctrl+Alt+N    ? Live-Tracking aus

Ctrl+Alt+K    ? Kanzel-Position anfahren

Ctrl+Alt+T    ? Taufstein-Position

Ctrl+Alt+O    ? Orgel-Position

Ctrl+Alt+?/?  ? Manuelle Kamerakorrektur
Ctrl+Alt+?/?  

Ctrl+Alt++/-  ? Zoom anpassen
```

## Hilfe während der Verwendung

Drücken Sie `Ctrl+Alt+H` um den Hilfedialog mit allen Tastaturkürzeln anzuzeigen.

## Problembehandlung

### Kamera reagiert nicht

1. **Netzwerkverbindung prüfen:**
   - Ping zur Kamera-IP: `ping 192.168.1.100`
   - Webinterface erreichbar? `http://192.168.1.100`

2. **Canon.cfg überprüfen:**
   - IP-Adresse korrekt?
   - Benutzername/Passwort korrekt?

3. **Firewall:**
   - Eventuell Firewall-Ausnahme für `CanonRemoteControl.exe` erstellen

### Tastaturkürzel funktionieren nicht

1. **Administrator-Rechte:**
   - Einige Anwendungen (inkl. PowerPoint) benötigen möglicherweise erhöhte Rechte
   - Versuchen Sie die Applikation als Administrator zu starten

2. **Konflikt mit anderen Hotkeys:**
   - Prüfen Sie ob andere Anwendungen dieselben Tastenkombinationen verwenden

### Statusmeldungen werden nicht angezeigt

1. **Bildschirmauflösung:**
   - Bei mehreren Monitoren: Applikation zeigt Status auf Hauptmonitor
   - Position kann in `StatusOverlay.xaml.cs` angepasst werden

## Deinstallation

1. Applikation beenden (über Taskleiste/Taskmanager)
2. Ordner mit `CanonRemoteControl.exe` löschen
3. Keine Registry-Einträge werden erstellt

## Erweiterte Konfiguration

### Weitere Preset-Positionen hinzufügen

Bearbeiten Sie `CanonCrn100Controller.cs` und fügen Sie neue Methoden hinzu:

```csharp
public async Task<bool> RecallChor()
{
    return await RecallPreset(5);
}
```

Registrieren Sie ein neues Hotkey in `GlobalHotKeyManager.cs`.

### Kamera-Geschwindigkeit anpassen

Die Pan/Tilt/Zoom-Geschwindigkeit kann in `CanonCrn100Controller.cs` angepasst werden:

```csharp
// Beispiel: PTS50 ? PTS30 für langsamere Bewegung
public async Task<bool> PanTiltUp()
{
    return await SendCommand("/cgi-bin/aw_ptz?cmd=%23PTS30&res=1");
}
```

Geschwindigkeitswerte: 01 (langsam) bis 99 (schnell), Standard: 50

## Support

Bei Fragen oder Problemen konsultieren Sie:
- Canon CRN-100 Bedienungsanleitung
- Diese README.md Dokumentation
- Projektdokumentation im Source Code
