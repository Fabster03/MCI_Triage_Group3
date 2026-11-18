# MCI Triage – Logikschicht

```
Scripts/Core      MciTriage.Core      reines C#, keine Unity-Abhängigkeit (noEngineReferences)
  Model/          PatientFindings, PatientDefinition (+ zeitgesteuerte Zustandsänderungen), Enums
  Triage/         StartTriageEngine – deterministische START-Referenzklassifikation inkl. Entscheidungspfad
  Logging/        EventLog – zentrales Event-Log, CSV-Export
  Scenario/       ScenarioDefinition, ScenarioSession (Timer, Aktionen, Logging), IClock
  Evaluation/     TriageEvaluator → SessionReport (Accuracy, Unter-/Übertriage, Zeit, Protokolltreue)
Scripts/Runtime   MciTriage.Runtime   Unity-Anbindung
                  PatientData / ScenarioData (ScriptableObjects), ScenarioController (MonoBehaviour)
Data/             Beispielszenario „Busunfall im Tunnel“ mit 5 Patienten
Tests/EditMode    NUnit-Tests (Window > General > Test Runner > EditMode)
```

## Datenfluss

UI/VR-Interaktion → `ScenarioController` → `ScenarioSession` → `EventLog`
→ bei Szenarioende `TriageEvaluator.Evaluate(scenario, log)` → `SessionReport`.

Die Auswertung arbeitet ausschließlich auf dem Event-Log. Gespeicherte Logs lassen sich
deshalb später neu auswerten, auch wenn sich Bewertungsregeln ändern.

## Benutzung in der Szene

1. Leeres GameObject anlegen, `ScenarioController` hinzufügen, `TunnelBusCrash` als Scenario zuweisen.
2. Interaktionsskripte rufen nur `ApproachPatient`, `Assess`, `PerformIntervention`,
   `AssignTriage` und `FinishScenario` am Controller auf.
3. Am Ende landen `*_events.csv` und `*_report.txt` in
   `Application.persistentDataPath/MciTriageLogs` (Windows: `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\`).

## Platzhalter-Szene

Menü **MCI Triage → Build Placeholder Incident** baut in der offenen Szene das Objekt `MCI_Incident`:
ScenarioController, Einsatz-Tafel (Briefing, START/FINISH, Timer, Report) und die 5 Patienten mit
`PatientAvatar` (Kapsel bzw. Charaktermodell, falls vorhanden). Erneutes Ausführen fragt nach und baut alles neu –
**außer der Gruppe `Vehicles`** (Bus- und PKW-Modelle inkl. Deformation), die erhalten bleibt.

Menü **MCI Triage → Apply Crash Deformation** (`Scripts/Editor/CrashDeformationBuilder.cs`) erzeugt die
Unfallschäden an Bus und PKW mit dem Deform-Package (Details in der Haupt-README).

- `PatientAvatar` überträgt die Befunde auf die Darstellung (stehen/liegen, Atembewegung, Hautfarbe,
  Triage-Karte). Für echte Figuren wird nur `ApplyVisuals` auf Animator-Parameter umgestellt.
- `PatientPanel` ist das Untersuchungsmenü (öffnet per „Examine“-Button oder Grip auf die Kapsel).
- UI wird zur Laufzeit per Code erzeugt (`UiKit`), daher im Editor erst im Play-Mode sichtbar.
- PlayMode-Smoke-Test (`Tests/PlayMode`) startet die MainScene und spielt einen Kurzdurchlauf über die Buttons.

## Bewertungsregeln (Kurzfassung)

- Referenz = START-Kategorie **zum Zeitpunkt der letzten Zuweisung** (Patienten können sich verschlechtern).
- Dringlichkeit: Rot > Gelb > Grün > Schwarz. Zugewiesen weniger dringlich = Untertriage, dringlicher = Übertriage.
- Accuracy = korrekt / alle Patienten; Unter-/Übertriage-Rate = Anteil an triagierten Patienten.
- Protokolltreue: Alle Schritte des START-Referenzpfads wurden vor dem Taggen geprüft
  (Perfusion zählt per Radialispuls *oder* Rekapillarisierungszeit).
