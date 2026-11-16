# MCI Triage Trainer – Gruppe 3

VR-Trainer für die Triage bei einem Massenanfall von Verletzten (MCI) nach dem **START-Protokoll**.
Projekt im Kurs *Simulation, VR and AI in Medicine*.

**Szenario:** Ein Reisebus ist in einem Straßentunnel mit einem PKW kollidiert. Der Trainee ist als
erster Rettungsdienst vor Ort und muss 5 Verletzte innerhalb von 5 Minuten untersuchen und triagieren.
Am Ende wertet das System die Entscheidungen gegen eine deterministische START-Referenz aus.

---

## Schnellstart

| | |
|---|---|
| Unity-Version | **6000.3.25f1** (Unity 6.3), URP |
| VR | OpenXR + XR Interaction Toolkit 3.3.2 (getestet für Meta Quest via Link) |
| Hauptszene | `Assets/Scenes/MainScene.unity` |

1. Repo klonen bzw. `git pull`.
2. Unity Hub → **Add → Add project from disk** → Repo-Ordner wählen → öffnen.
   Der erste Import dauert einige Minuten (der `Library`-Ordner wird lokal neu aufgebaut).
3. `Assets/Scenes/MainScene` öffnen → **Play**.
4. Mit dem Controller-Strahl auf **START** an der Tafel zielen und den Trigger drücken. Dann zu den Patienten
   gehen, **Examine** anklicken (oder per Grip auf die Kapsel greifen), untersuchen und Triage-Karte vergeben.
   **FINISH** beendet das Szenario und zeigt die Auswertung.

### Testen ohne VR-Brille (XR Interaction Simulator)

Ist beim Drücken auf Play **keine Brille verbunden**, startet automatisch der *XR Interaction Simulator*
(nur im Editor, nicht im Build). Er simuliert Headset und Controller mit Maus und Tastatur – man testet also
genau dieselbe Interaktion wie später in VR. Unten links im Game-Fenster blendet er eine Tastenübersicht ein.

| Aktion | Taste |
|---|---|
| Laufen vor / zurück / seitlich | **W / S / A / D** |
| Hoch / runter | **E / Q** |
| Umsehen | **rechte Maustaste halten** + Maus bewegen |
| Zwischen Kopf, linkem und rechtem Controller wechseln | **Tab** |
| Controller-Strahl zielen | Controller per **Tab** wählen, dann Maus bewegen |
| **Trigger** (Buttons klicken) | **T** |
| **Grip** (Patienten-Kapsel greifen → Menü öffnet sich) | **G** |
| Position / Ansicht zurücksetzen | **R** |

**Typischer Testablauf:** Tab bis der rechte Controller aktiv ist → Strahl auf **START** → **T** →
mit WASD zu einem Patienten → Strahl auf **Examine** → **T** → Untersuchungen und Triage-Farbe jeweils mit **T** anklicken →
zurück zur Tafel → **FINISH** → Report lesen.

> Die Tasten beziehen sich auf die Position auf einer **englischen** Tastatur. Auf deutschen Tastaturen sind
> u. a. **Y und Z vertauscht**; WASD, T, G, Tab und R passen.
>
> Einstellung: `Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset`
> (*Project Settings → XR Plug-in Management → XR Interaction Toolkit*). Das Sample liegt unter
> `Assets/Samples/XR Interaction Toolkit/3.3.2/XR Interaction Simulator/`.

### Mit Meta Quest

Quest per **Link** (Kabel) oder **Air Link** mit dem PC verbinden (Meta-Quest-Link-App muss laufen, in der Brille
Link aktivieren), dann in Unity **Play**. Der Simulator startet in dem Fall nicht.

---

## Was ist neu? (Stand: 16.11.2026)

| Commit | Inhalt |
|---|---|
| `29cbcfd` | **Triage-Logik:** Patientenmodell, START-Engine, Szenario-Session mit Timer, Event-Log, Auswertung, Beispielszenario, 30 Unit-Tests |
| `4fa8744` | Automatische Unity-6.3-Upgrades (Materialien/Settings) – keine inhaltlichen Änderungen |
| `8e4e68d` | **Platzhalter-Szene:** Bus + PKW, 5 Patienten-Kapseln, Untersuchungsmenü, Triage-Karten, Einsatz-Tafel mit Timer und Report, PlayMode-Test |
| *neu* | **Testen ohne Brille:** XR Interaction Simulator; **Tunnel-Collider aktiviert** (vorher fiel man durch den Boden); alte `Main Camera` deaktiviert |

Alles Projekteigene liegt unter **`Assets/MciTriage/`**. Details zur internen Struktur:
[`Assets/MciTriage/README.md`](Assets/MciTriage/README.md).

---

## Wie es funktioniert

```
 VR / UI                     Bindeglied                    Logik (reines C#)
┌────────────────────┐     ┌──────────────────────┐     ┌──────────────────────────────┐
│ Patienten-Kapseln  │◀────│ PatientAvatar        │◀────│ ScenarioSession              │
│ Untersuchungsmenü  │────▶│ PatientPanel         │────▶│  • Befunde zur aktuellen Zeit│
│ Einsatz-Tafel      │     │ ScenarioBoard        │     │  • Timer / Zeitlimit         │
└────────────────────┘     │ ScenarioController   │     │  • EventLog (alle Aktionen)  │
                           └──────────────────────┘     ├──────────────────────────────┤
                                                        │ StartTriageEngine (Referenz) │
                                                        │ TriageEvaluator (Report)     │
                                                        └──────────────────────────────┘
```

- **Logik → Darstellung:** `PatientAvatar` liest jeden Frame die aktuellen Befunde und überträgt sie auf die
  Figur (steht/liegt, Atembewegung im Takt der Atemfrequenz, Hautfarbe, Triage-Karte auf der Brust).
- **Darstellung → Logik:** Jeder Button im Untersuchungsmenü ruft eine Funktion am `ScenarioController` auf.
  Diese gibt den Befund zurück und schreibt die Aktion mit Zeitstempel ins Event-Log.
- **Auswertung:** Bei Szenarioende berechnet `TriageEvaluator` aus dem Event-Log den Report. Event-Log (CSV) und
  Report (TXT) werden gespeichert unter
  `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\MciTriageLogs\`.

Die Logik (`Scripts/Core`) hat **keine Unity-Abhängigkeit** und ist vollständig getestet. Die Darstellung kann
ausgetauscht werden (z. B. Mixamo-Figuren statt Kapseln), ohne die Logik anzufassen.

---

## Die Patienten

Die Kategorie wird **nicht eingetragen**, sondern von der START-Engine aus den Werten berechnet.
Werte sind ein erster Entwurf und sollten fachlich geprüft werden.

| | Beschreibung | Schlüsselbefund | START |
|---|---|---|---|
| P01 | Mann ~35, läuft herum, stark blutende Kopfplatzwunde | gehfähig | 🟢 Grün (Falle für Übertriage) |
| P02 | Frau ~58, offene Unterschenkelfraktur, blutet | AF 24, Puls tastbar, folgt Aufforderungen | 🟡 Gelb → 🔴 Rot nach 150 s, **außer Blutung wurde gestillt** |
| P03 | Busfahrer ~45, Thoraxtrauma | AF 36/min | 🔴 Rot |
| P04 | Frau ~22, reglos, keine sichtbare Atmung | atmet erst nach Atemwege freimachen | 🔴 Rot |
| P05 | Mann ~70, eingeklemmt, schweres Kopftrauma | atmet auch nach Freimachen nicht | ⚫ Schwarz |

**Werte ändern:** In Unity eine Datei in `Assets/MciTriage/Data/Patients/` anklicken → Inspector.
**Neuer Patient:** Rechtsklick → *Create → MCI Triage → Patient*, dann im Szenario `TunnelBusCrash` eintragen.

---

## Auswertung (Messgrößen)

- **Accuracy:** korrekt triagiert / alle Patienten
- **Unter- / Übertriage-Rate** (Dringlichkeit Rot > Gelb > Grün > Schwarz), kritische Untertriage gesondert
- **Entscheidungszeit** pro Patient (erster Kontakt bis Triage)
- **Protokolltreue:** wurden alle START-Schritte vor dem Taggen geprüft? Fehlende Schritte werden aufgelistet
- **Unnötige Untersuchungen** (z. B. Herzfrequenz, SpO₂ – nicht Teil von START)
- **Reihenfolge** der Triage und **versäumte Re-Triage** (Patient hat sich nach dem Taggen verschlechtert)

NASA-TLX und Usability-Fragebögen sind noch nicht integriert.

---

## Tests

Unity → *Window → General → Test Runner*

- **EditMode (30 Tests):** START-Zweige inkl. Grenzwerte, Zeitlimit, Verschlechterung, Unter-/Übertriage, Protokolltreue
- **PlayMode (1 Test):** lädt `MainScene`, klickt START, untersucht/triagiert zwei Patienten, FINISH, prüft den Report

---

## Wichtige Hinweise fürs Team

- **Platzhalter-Szene neu bauen:** Menü *MCI Triage → Build Placeholder Incident* ersetzt das Objekt
  `MCI_Incident` in der offenen Szene komplett. Manuelle Änderungen darunter gehen dabei verloren.
- **UI entsteht erst zur Laufzeit** (per Code). Im Editor ohne Play sieht man nur Quader und Kapseln.
- **Merge-Konflikte in Szenen** lassen sich kaum auflösen → absprechen, wer gerade an `MainScene.unity` arbeitet.
- **Nur über Unity verschieben/umbenennen**, nicht im Explorer – sonst passen die `.meta`-Dateien nicht mehr.
- **Assets** (Mixamo-Modelle, Animationen) unter `Assets/MciTriage/Art/` ablegen; keine ZIPs committen.
- **Tunnel-Collider nicht deaktivieren:** Die drei Box Collider an `DasTunnelAsset` sind Boden und Wände – ohne sie
  fällt das XR-Rig durch den Boden. Der Mesh Collider bleibt aus (*Convex* würde den Tunnel als Block füllen).
- **„Modified“-Dateien ohne Inhalt:** Unity speichert Dateien mit anderen Zeilenenden (LF statt CRLF). Zeigt
  `git diff` für eine Datei nichts an, ist es nur das – mit `git restore <datei>` zurücksetzen oder ignorieren.

---

## Stand gegenüber MVP

| MVP-Anforderung | Stand |
|---|---|
| Ein Incident mit 3–5 Patienten | ✅ Tunnel, Bus + PKW, 5 Patienten |
| START-Protokoll, deterministische Referenz | ✅ `StartTriageEngine` |
| Patientenkarten mit Vitalwerten | ✅ Untersuchungsmenü pro Patient |
| Zeitgesteuerte Triage-Entscheidungen | ✅ Zeitlimit 5 min, Zeit pro Patient |
| Event-Logging | ✅ zentrales Log, CSV-Export |
| Ergebnis-Dashboard | 🟡 Text-Report auf der Tafel; grafisches Dashboard fehlt |
| Zustandsänderungen | ✅ P02 verschlechtert sich (Erweiterung) |

## Offene Punkte / nächste Schritte

- [x] Interaktion im Simulator getestet (Laufen, Buttons per Strahl, Triage, Report)
- [ ] Test mit echtem Headset: Lesbarkeit, Wege im Tunnel, Bedienkomfort
- [x] Alte `Main Camera` deaktiviert (ggf. ganz löschen)
- [x] Tunnel-Collider (Boden + Wände) aktiviert
- [ ] Mixamo-Figuren + Animator statt Kapseln (`PatientAvatar.ApplyVisuals` umstellen)
- [ ] Grafisches Ergebnis-Dashboard (Debriefing)
- [ ] Patientenwerte fachlich prüfen / mit Quelle belegen
- [ ] Teilnehmer-ID vor Start eingeben (für die Evaluation)
- [ ] NASA-TLX- und Usability-Fragebogen
- [x] XR Interaction Simulator zum Testen ohne Brille
- [ ] `.gitattributes` für Unity (verhindert Phantom-Änderungen durch Zeilenenden LF/CRLF)
- [ ] Aufräumen: `quick-tunnel.zip` und `quick-tunnel/` im Root sind Duplikate von `Assets/Tunnel/`
