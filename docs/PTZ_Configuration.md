# Canon PTZ Kamera Konfiguration für JokiAutomation

## Übersicht

JokiAutomation verwendet **die gleiche PTZ-Verbindungslogik** wie die CanonRemoteControl-App. Beide Anwendungen teilen sich:
- Die gleiche `Network.cfg` Konfigurationsdatei
- Die gleichen PTZ-Controller (`XcCanonPtzController` und `LegacyAwPtzController`)
- Den gleichen `SharedPresetState` für Preset-Synchronisation

## Network.cfg Format für Canon PTZ

### Vollständiges Format:
```
CANON_CRN100;IPAddress;Port;Username;Password;Protocol;PanSpeed;TiltSpeed;ZoomSpeed
```

### Parameter:
- **IPAddress**: IP-Adresse der Kamera (z.B. `192.168.178.60`)
- **Port**: HTTP/HTTPS Port (Standard: `80`, HTTPS: `443`)
- **Username**: Admin-Benutzername (Standard: `admin`)
- **Password**: Admin-Passwort (z.B. `12345`)
- **Protocol**: Protokoll-Typ
  - `XC` - Empfohlen für Canon CR-N100/300/500/700
  - `AW` - Legacy für ältere Modelle
- **PanSpeed**: Pan-Geschwindigkeit (1-1500, empfohlen: 1500)
- **TiltSpeed**: Tilt-Geschwindigkeit (1-1500, empfohlen: 900)
- **ZoomSpeed**: Zoom-Geschwindigkeit (1-100, empfohlen: 70)

### Beispiele:

**Minimale Konfiguration:**
```
CANON_CRN100;192.168.1.100;80;admin;password;XC
```

**Vollständige Konfiguration:**
```
CANON_CRN100;192.168.178.60;80;admin;12345;XC;1500;900;70
```

**HTTPS Konfiguration:**
```
CANON_CRN100;192.168.178.60;443;admin;12345;XC;1500;900;70
```

## PTZ-Modus aktivieren

In der `Network.cfg` muss zusätzlich der PTZ-Modus aktiviert werden:

```
PTZ_CAM;true
```

Wenn `PTZ_CAM = false`, verwendet JokiAutomation den RaspberryPi-Motor für Kamera-Positionierung.

## Unterschied zwischen JokiAutomation und CanonRemoteControl

| Feature | JokiAutomation | CanonRemoteControl |
|---------|----------------|-------------------|
| **PTZ Position Move** | ? Über GUI-Button "Start" | ? Über Hotkeys (Strg+Shift+T/A/K/O/E/G/N) |
| **Manuelle PTZ-Steuerung** | ? Nicht verfügbar | ? Über Pfeiltasten + Strg |
| **Preset Speichern** | ? Nur manuell an Kamera | ? Nur manuell an Kamera |
| **Auto-Reconnect** | ? Automatisch bei Move-Versuch | ? Manueller Neustart nötig |
| **RasPi IR/Audio** | ? Immer aktiv | ? Nicht verfügbar |
| **Shared Preset State** | ? Schreibt letzte Position | ? Schreibt/Liest letzte Position |

## Preset-Synchronisation zwischen Apps

Beide Apps verwenden `SharedPresetState` über eine Temp-Datei:
- **Speicherort**: `%TEMP%\CanonPtzLastPreset.txt`
- **Inhalt**: Letzte abgerufene Preset-Nummer (1-100)

**Beispiel:**
1. JokiAutomation ruft Preset 5 ab ? Schreibt `5` in Temp-Datei
2. CanonRemoteControl kann diese Info lesen und anzeigen
3. CanonRemoteControl ruft Preset 8 ab ? Schreibt `8` in Temp-Datei
4. JokiAutomation kann diese Info lesen

## Fehlerbehebung

### Kamera nicht verbunden

**Symptom:** 
```
? Canon CR-N100 Verbindungsfehler: Connection refused
```

**Lösungen:**
1. **Kamera einschalten** - Überprüfen Sie den Power-Status
2. **Netzwerk prüfen** - Ping testen: `ping 192.168.178.60`
3. **IP-Adresse prüfen** - Im Kamera-Menü: Network ? IP Address
4. **Browser-Test** - Öffnen Sie `http://192.168.178.60/` im Browser
5. **Port prüfen** - Standard ist 80, kann aber 443 (HTTPS) sein
6. **Firewall** - Erlauben Sie ausgehende Verbindungen auf Port 80/443

### Falsches Protokoll

**Symptom:**
```
? Canon CR-N100 Verbindungsfehler (XC): Invalid response
```

**Lösung:** Ändern Sie Protocol von `XC` zu `AW` oder umgekehrt:
```
CANON_CRN100;192.168.178.60;80;admin;12345;AW
```

### Falsches Passwort

**Symptom:**
```
? Canon CR-N100 Verbindungsfehler: 401 Unauthorized
```

**Lösung:** 
1. Überprüfen Sie das Admin-Passwort in der Kamera
2. Standard-Passwort ist oft leer oder `admin`
3. Korrigieren Sie den Password-Parameter in `Network.cfg`

### Auto-Reconnect nutzen

Wenn die Verbindung beim Start fehlschlägt, können Sie:
1. Position im GUI auswählen
2. "Start"-Button klicken
3. JokiAutomation versucht automatisch neu zu verbinden
4. Bei Erfolg wird die Position sofort abgerufen

## Beispiel Network.cfg (Komplett)

```ini
# ===== NETWORK DEVICES =====
ATEM_MiniPro;192.168.178.48
RaspberryPi_Main;192.168.178.50;22

# Canon PTZ Camera (vollständige Konfiguration)
CANON_CRN100;192.168.178.60;80;admin;12345;XC;1500;900;70

# ===== PTZ CAMERA SETTINGS =====
PTZ_CAM;true

# ===== USER CREDENTIALS =====
USER_Admin;AdminPassword123
USER_SuperUser;SuperUserPass456
```

## Log-Ausgaben

### Erfolgreiche Verbindung:
```
JokiAutomation
Lade Canon PTZ Konfiguration aus Network.cfg...
Konfiguration geladen:
  IP: 192.168.178.60:80
  User: admin
  Protocol: XC
  HTTPS: False
Verwende XC Protocol Controller
Verbinde mit Canon PTZ: 192.168.178.60:80...
? Canon CR-N100 verbunden (XC Protocol)
Position Control: Canon PTZ (Kamera-Positionierung) + RasPi IR-Steuerung
UI: PTZ-Modus - Move-Button aktiv, Teach-Buttons deaktiviert
```

### Fehlgeschlagene Verbindung mit Auto-Reconnect:
```
JokiAutomation
? PTZ-Kamera nicht verbunden!
Versuche automatisch neu zu verbinden...
Lade Canon PTZ Konfiguration aus Network.cfg...
? Canon CR-N100 verbunden (XC Protocol)
? PTZ-Kamera erfolgreich verbunden!
Bewege PTZ-Kamera zu Position 5: Altar
PositionControl
PTZ-Modus: Sende Preset 5 an Canon Kamera
Preset 5 erfolgreich abgerufen
```

## Weitere Informationen

- **Canon CR-N100 Handbuch**: Kapitel "Network Settings" für IP-Konfiguration
- **CanonRemoteControl App**: Für erweiterte PTZ-Steuerung mit Hotkeys
- **SharedPresetState**: Temp-Datei für App-übergreifende Preset-Synchronisation
- **Network.cfg**: Zentrale Konfigurationsdatei für alle Netzwerk-Geräte
