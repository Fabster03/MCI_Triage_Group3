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

## Bewertungsregeln (Kurzfassung)

- Referenz = START-Kategorie **zum Zeitpunkt der letzten Zuweisung** (Patienten können sich verschlechtern).
- Dringlichkeit: Rot > Gelb > Grün > Schwarz. Zugewiesen weniger dringlich = Untertriage, dringlicher = Übertriage.
- Accuracy = korrekt / alle Patienten; Unter-/Übertriage-Rate = Anteil an triagierten Patienten.
- Protokolltreue: Alle Schritte des START-Referenzpfads wurden vor dem Taggen geprüft
  (Perfusion zählt per Radialispuls *oder* Rekapillarisierungszeit).
