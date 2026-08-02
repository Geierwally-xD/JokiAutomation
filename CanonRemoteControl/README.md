# Canon CRN-100 Remote Control

Eine WPF-Applikation zur Steuerung der Canon CRN-100 PTZ-Kamera während PowerPoint-Präsentationen über globale Tastaturkürzel.

## Features

- **Globale Tastaturkürzel** funktionieren auch bei aktiven PowerPoint-Präsentationen
- **Statusanzeige** im oberen rechten Bildschirmbereich (Overlay über allen Fenstern)
- **Live-Tracking** Steuerung mit persistenten Statusmeldungen
- **Preset-Positionen** für häufig verwendete Kamerapositionen
- Läuft unsichtbar im Hintergrund

## Tastaturkürzel

### Kamerabewegung (Pan/Tilt)
- `Ctrl+Alt+?` - Kamera nach oben
- `Ctrl+Alt+?` - Kamera nach unten
- `Ctrl+Alt+?` - Kamera nach links
- `Ctrl+Alt+?` - Kamera nach rechts

### Zoom
- `Ctrl+Alt++` - Zoom hinein
- `Ctrl+Alt+-` - Zoom heraus

### Preset-Positionen
- `Ctrl+Alt+T` - Taufsteinposition (Preset 1)
- `Ctrl+Alt+A` - Altarposition (Preset 2)
- `Ctrl+Alt+K` - Kanzelposition (Preset 3)
- `Ctrl+Alt+O` - Orgelposition (Preset 4)

### Live-Tracking
- `Ctrl+Alt+E` - Live-Track Einzelperson aktivieren
- `Ctrl+Alt+G` - Live-Track Gruppe aktivieren
- `Ctrl+Alt+N` - Live-Tracking deaktivieren

### Hilfe
- `Ctrl+Alt+H` - Hilfedialog anzeigen

**Hinweis:** Alle Tastenkombinationen funktionieren sowohl mit Groß- als auch mit Kleinbuchstaben.

## Konfiguration

Vor dem ersten Start muss die IP-Adresse der Canon CRN-100 in `MainWindow.xaml.cs` angepasst werden:

```csharp
_controller = new CanonCrn100Controller("192.168.1.100");
```

Falls die Kamera mit Benutzername/Passwort geschützt ist:

```csharp
_controller = new CanonCrn100Controller("192.168.1.100", "admin", "passwort");
```

## Preset-Zuordnung

Die Preset-Nummern können in den Methoden `RecallTaufstein()`, `RecallAltar()`, `RecallKanzel()` und `RecallOrgel()` in der Klasse `CanonCrn100Controller` angepasst werden.

Standardzuordnung:
- Preset 1 = Taufstein
- Preset 2 = Altar
- Preset 3 = Kanzel
- Preset 4 = Orgel

## Technische Details

- **.NET Framework 4.8** WPF-Applikation
- Verwendet **Windows API** für globale Hotkeys (RegisterHotKey)
- **HTTP-basierte** Kommunikation mit der Canon CRN-100
- **Topmost-Overlay** für Statusmeldungen
- Automatisches **Ausblenden** der Statusmeldungen nach 5 Sekunden (außer bei aktivem Live-Tracking)

## Verwendung mit PowerPoint

1. Applikation starten
2. Das Hauptfenster minimiert sich automatisch in die Taskleiste
3. PowerPoint-Präsentation starten (Vollbildmodus)
4. Tastaturkürzel funktionieren auch im Präsentationsmodus
5. Statusmeldungen werden über der Präsentation eingeblendet

## Hinweise

- Die Applikation läuft im Hintergrund und kann über das Systray-Icon geschlossen werden
- Bei aktivem Live-Tracking bleibt die Statusmeldung sichtbar bis zum Deaktivieren (Ctrl+Alt+N)
- Das Hauptfenster kann über die Taskleiste wieder eingeblendet werden
