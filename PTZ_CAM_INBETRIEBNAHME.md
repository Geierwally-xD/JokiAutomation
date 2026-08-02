# Inbetriebnahme Canon CR-N100 (PTZ_CAM Modus)

## Ziel

Wenn `PTZ_CAM=true` gesetzt ist:

- die GUI-Tabs `Position Camcorder` und `AutoZoom Konfig` werden ausgeblendet
- Kamera-Fahrkommandos laufen direkt zur Canon CR-N100 (Preset Recall)
- Teach/Zoom-Konfiguration entfällt im Automatikbetrieb (Teach direkt auf der Kamera)

---

## 1. Voraussetzungen

- Canon CR-N100 im Netzwerk erreichbar
- HTTP API / Remote-Control auf der Kamera aktiviert
- Zugangsdaten vorhanden (falls Auth aktiv)
- JoKiAutomation mit aktueller PTZ-Erweiterung gebaut

---

## 2. Network.cfg konfigurieren

Beispiel:

```
PTZ_CAM;true
CANON_CRN100;192.168.1.120;80;admin;passwort
PTZ_PRESET_RECALL_PATH;/api/ptz/preset/{0}/call
PTZ_PRESET_RECALL_PATHS;/api/ptz/preset/{0}/recall|/-wvhttp-01-/control.cgi?p=ptz_recall_preset&num={0}|/cgi-bin/ptzctrl.cgi?ptzcmd&poscall&{0}
PTZ_SELFTEST;true
PTZ_SELFTEST_PRESET;1
```

Hinweise:

- `{0}` wird automatisch mit der Preset-Nummer ersetzt
- intern wird Positionsindex `0..n` auf Preset `1..n+1` gemappt
- bei Fehlern testet die App automatisch Fallback-Endpoints (GET und POST)

---

## 3. Presets auf der Kamera anlegen

1. Kamera-Weboberfläche öffnen
2. Gewünschte Positionen als Presets speichern (Reihenfolge beachten)
3. Preset 1 entspricht Positionseintrag 0 in `PositionControl.cfg`, Preset 2 -> Eintrag 1, usw.

---

## 4. Funktionsprüfung

1. App starten
2. Log prüfen:
   - `PTZ_CAM = True`
   - `Canon CR-N100 API initialisiert (...)`
3. Kommando testen:
   - `PositionControl <Positionsname> <Profil>`
4. Erwartung:
   - Preset Recall an Canon erfolgreich
   - keine RasPi-Positionierungsfahrt

---

## 5. Fehleranalyse

Wenn Preset-Aufruf fehlschlägt:

- IP/Port prüfen
- Benutzer/Passwort prüfen
- Kamera-API aktiv?
- Endpoint in `PTZ_PRESET_RECALL_PATH` prüfen
- Fallbacks in `PTZ_PRESET_RECALL_PATHS` ergänzen
- Logeinträge mit HTTP-Status/Response auswerten

---

## 6. Rückbau auf RasPi-Betrieb

In `Network.cfg`:

```
PTZ_CAM;false
```

Danach:

- Tabs `Position Camcorder` und `AutoZoom Konfig` wieder sichtbar
- Position/Zoom laufen wieder über Raspberry Pi

---

## 7. Optionaler Selbsttest beim Start

Über `Network.cfg` aktivierbar:

```
PTZ_SELFTEST;true
PTZ_SELFTEST_PRESET;1
```

Bedeutung:

- `PTZ_SELFTEST=true`: Beim App-Start wird ein Preset-Recall ausgelöst
- `PTZ_SELFTEST_PRESET=1`: 1-basierte Preset-Nummer

Log-Ausgaben:

- `PTZ Selbsttest starte (Preset X)...`
- `PTZ Selbsttest erfolgreich.`
- oder `PTZ Selbsttest fehlgeschlagen.`
