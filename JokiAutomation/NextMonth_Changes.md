# Geplante Änderungen (nächster Monat)

## 1) Parallelbetrieb GUI + CLI in JokiAutomation

### Ziel
GUI und Kommandozeilen-Aufrufe sollen gleichzeitig nutzbar sein, ohne konkurrierende Hardware-Steuerung oder doppelte Prozesslogik.

### Aktueller Stand
- In `JokiAutomation` ist ein globaler Single-Instance-Mutex aktiv (`Global\JokiAutomation_SingleInstance`).
- Dadurch blockiert eine laufende GUI aktuell den parallelen CLI-Prozess.

### Geplante Umsetzung
- Empfohlener Weg: **IPC-Ansatz (Variante 3, MVP)**
  - GUI bleibt führende Instanz.
  - CLI startet keinen zweiten Hardware-Prozess, sondern sendet Kommando an GUI (z. B. Named Pipe).
  - GUI verarbeitet Kommando über bestehenden `CommandInterpreterAsync`-Pfad.

### Mindestumfang (MVP)
1. Named-Pipe-Server in GUI-Instanz starten.
2. CLI-Mode erkennt laufende Instanz und sendet Kommando per Pipe.
3. GUI bestätigt Empfang (optional einfacher ACK-Text).
4. CLI beendet sich mit aussagekräftigem ExitCode.

### Nutzen
- Kein paralleler Zugriff auf ATEM/RasPi/PTZ.
- Stabilerer Ablauf als bei zwei Prozessen.
- Benutzer kann GUI offen lassen und trotzdem CLI-triggern.

---

## 2) PTZ RemoteControl + JokiAutomation Koordination

### Ziel
Konfliktfreie PTZ-Steuerung zwischen `CanonRemoteControl` und `JokiAutomation`.

### Aktueller Stand
- `SharedSessionState` teilt Session-ID (Mutex + MemoryMappedFile), aber ist **kein globaler Befehls-Lock**.
- Beide Apps können prinzipiell PTZ-Befehle parallel senden.

### Geplante Umsetzung
- **Gemeinsamer globaler PTZ-Command-Mutex** in beiden Apps (minimal sauber).
- Lock beim Senden/Starten PTZ-relevanter Befehle erwerben.
- Bei belegt: kurzer Timeout + klare Meldung + optional Retry.

### Mindestumfang
1. Gemeinsamen Mutex-Namen definieren (z. B. `Global\CanonPtzCommandMutex`).
2. Lock-Helfer in gemeinsam nutzbarem Bereich (`CanonPtzCommon`) ergänzen.
3. In beiden Apps vor PTZ-Commandpfaden verwenden.
4. Logging für "Lock belegt", "Lock erworben", "Lock freigegeben" ergänzen.

### Nutzen
- Keine konkurrierenden PTZ-Kommandos.
- Deterministischeres Kameraverhalten.
- Geringerer Fehleranteil bei Live-Umschaltungen.

---

## Prioritätsempfehlung
1. GUI/CLI-Koordination per IPC (MVP)
2. PTZ-Command-Mutex in beiden Apps

Diese Reihenfolge reduziert zuerst Prozesskonflikte, danach Geräte-/Befehlskonflikte.
