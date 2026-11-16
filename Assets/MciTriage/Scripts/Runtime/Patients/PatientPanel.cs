using System.Collections.Generic;
using MciTriage.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MciTriage.Runtime
{
    /// <summary>
    /// Floating examination menu of one patient: assessments, life-saving
    /// interventions and triage tags. Every button calls the ScenarioController,
    /// which logs the action and returns the observed finding.
    /// Only one panel is open at a time.
    /// </summary>
    public class PatientPanel : MonoBehaviour
    {
        private const float Width = 900;
        private const float Height = 1210;
        private const float MetersPerPixel = 0.0009f;

        private static PatientPanel _open;

        private static readonly (AssessmentType type, string label)[] Assessments =
        {
            (AssessmentType.Ambulation, "Ask to walk"),
            (AssessmentType.Breathing, "Check breathing"),
            (AssessmentType.RespiratoryRate, "Count respiratory rate"),
            (AssessmentType.RadialPulse, "Feel radial pulse"),
            (AssessmentType.CapillaryRefill, "Capillary refill"),
            (AssessmentType.MentalStatus, "Simple command"),
            (AssessmentType.HeartRate, "Heart rate"),
            (AssessmentType.OxygenSaturation, "SpO2"),
        };

        private static readonly (InterventionType type, string label)[] Interventions =
        {
            (InterventionType.OpenAirway, "Open airway"),
            (InterventionType.ControlHemorrhage, "Control bleeding"),
        };

        private static readonly (TriageCategory category, string label)[] Tags =
        {
            (TriageCategory.Immediate, "RED"),
            (TriageCategory.Delayed, "YELLOW"),
            (TriageCategory.Minor, "GREEN"),
            (TriageCategory.Expectant, "BLACK"),
        };

        private PatientAvatar _avatar;
        private GameObject _root;
        private Text _timer;
        private Text _status;
        private Text _tagText;
        private readonly Dictionary<AssessmentType, Text> _assessmentResults = new Dictionary<AssessmentType, Text>();
        private readonly Dictionary<InterventionType, Text> _interventionResults = new Dictionary<InterventionType, Text>();
        private ScenarioSession _shownSession;

        private ScenarioController Controller => _avatar.Controller;

        public bool IsOpen => _root != null && _root.activeSelf;

        public void Init(PatientAvatar avatar)
        {
            _avatar = avatar;
            Build();
            _root.SetActive(false);
        }

        public void Open()
        {
            if (_open != null && _open != this) _open.Close();
            _open = this;

            // Place between patient and trainee, facing the trainee.
            var cam = SceneCamera.Get();
            var patientPos = _avatar.transform.position;
            var toCam = cam != null ? cam.transform.position - patientPos : -_avatar.transform.forward;
            toCam.y = 0;
            toCam = toCam.sqrMagnitude > 1e-4f ? toCam.normalized : Vector3.forward;
            _root.transform.position = patientPos + toCam * 0.9f + Vector3.up * 1.35f;
            _root.SetActive(true);
            UiKit.FaceCamera(_root.transform);

            if (Controller.IsRunning) Controller.ApproachPatient(_avatar.PatientId);
            SetStatus(Controller.IsRunning ? "" : "Scenario not running - press START on the board.");
        }

        public void Close()
        {
            _root.SetActive(false);
            if (_open == this) _open = null;
        }

        private void Update()
        {
            if (!_root.activeSelf) return;

            var session = Controller.Session;
            if (session != _shownSession) ResetResults(session); // new run started

            _timer.text = session == null ? "not started"
                : session.IsRunning ? "Time left " + UiKit.FormatTime(session.RemainingSeconds)
                : "Scenario ended";

            var assigned = session != null ? session.GetAssignedCategory(_avatar.PatientId) : TriageCategory.None;
            _tagText.text = assigned == TriageCategory.None ? "Tag: -" : "Tag: " + assigned.ColorName().ToUpperInvariant();
            _tagText.color = assigned == TriageCategory.None ? UiKit.MutedText : TriageColors.Get(assigned);
            if (assigned == TriageCategory.Expectant) _tagText.color = UiKit.TextColor;
        }

        // ------------------------------------------------------------------ actions

        private void OnAssess(AssessmentType type)
        {
            var value = Controller.Assess(_avatar.PatientId, type);
            if (value == null) { SetStatus("Scenario not running."); return; }
            _assessmentResults[type].text = FormatAssessment(type, value);
            SetStatus("");

            // Walking patients show it: the panel closes, the patient walks a few steps and the panel reopens.
            if (type == AssessmentType.Ambulation && value == "yes" && _avatar.CanDemonstrateWalk)
            {
                Close();
                _avatar.DemonstrateWalk(() => { if (_open == null) Open(); });
            }
        }

        private void OnIntervene(InterventionType type)
        {
            var value = Controller.PerformIntervention(_avatar.PatientId, type);
            if (value == null) { SetStatus("Scenario not running."); return; }
            _interventionResults[type].text = type == InterventionType.OpenAirway
                ? (value == "yes" ? "breathing now" : "still not breathing")
                : "done";
            SetStatus("");
        }

        private void OnTag(TriageCategory category)
        {
            if (!Controller.AssignTriage(_avatar.PatientId, category)) { SetStatus("Scenario not running."); return; }
            SetStatus("Tagged " + category.ColorName().ToUpperInvariant() + ".");
        }

        private void SetStatus(string text) => _status.text = text;

        private void ResetResults(ScenarioSession session)
        {
            _shownSession = session;
            foreach (var t in _assessmentResults.Values) t.text = "";
            foreach (var t in _interventionResults.Values) t.text = "";
        }

        private static string FormatAssessment(AssessmentType type, string v)
        {
            bool yes = v == "yes";
            switch (type)
            {
                case AssessmentType.Ambulation: return yes ? "walks" : "cannot walk";
                case AssessmentType.Breathing: return yes ? "breathing" : "NOT breathing";
                case AssessmentType.RespiratoryRate: return v + " /min";
                case AssessmentType.RadialPulse: return yes ? "palpable" : "absent";
                case AssessmentType.CapillaryRefill: return v + " s";
                case AssessmentType.MentalStatus: return yes ? "follows commands" : "does not follow";
                case AssessmentType.HeartRate: return v == "0" ? "no pulse" : v + " /min";
                case AssessmentType.OxygenSaturation: return v == "0" ? "no reading" : v + " %";
                default: return v;
            }
        }

        // ------------------------------------------------------------------ layout

        private void Build()
        {
            var canvas = UiKit.CreateWorldCanvas("ExaminationPanel", null, new Vector2(Width, Height), MetersPerPixel);
            _root = canvas.gameObject;
            var t = _root.transform;
            var p = _avatar.Patient;

            UiKit.Panel(t, 0, 0, Width, Height, UiKit.PanelColor);

            float y = 20;
            UiKit.Label(t, 30, y, 520, 56, p.displayName, 44, TextAnchor.MiddleLeft, FontStyle.Bold);
            _timer = UiKit.Label(t, 560, y, 220, 56, "", 28, TextAnchor.MiddleRight, FontStyle.Normal, UiKit.MutedText);
            UiKit.Button(t, Width - 100, y, 70, 56, "X", 32, Close);
            y += 66;

            UiKit.Label(t, 30, y, Width - 60, 120, p.description, 26, TextAnchor.UpperLeft, FontStyle.Italic, UiKit.MutedText);
            y += 130;

            y = Section(t, y, "Assessment");
            foreach (var (type, label) in Assessments)
            {
                var captured = type;
                UiKit.Button(t, 30, y, 420, 58, label, 28, () => OnAssess(captured));
                _assessmentResults[type] = UiKit.Label(t, 475, y, Width - 505, 58, "", 30, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 66;
            }

            y = Section(t, y + 6, "Life-saving interventions");
            foreach (var (type, label) in Interventions)
            {
                var captured = type;
                UiKit.Button(t, 30, y, 420, 58, label, 28, () => OnIntervene(captured));
                _interventionResults[type] = UiKit.Label(t, 475, y, Width - 505, 58, "", 30, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 66;
            }

            y = Section(t, y + 6, "Triage tag");
            float tagWidth = (Width - 60 - 3 * 16) / 4f;
            for (int i = 0; i < Tags.Length; i++)
            {
                var (category, label) = Tags[i];
                UiKit.Button(t, 30 + i * (tagWidth + 16), y, tagWidth, 80, label, 32, () => OnTag(category),
                    TriageColors.Get(category), TriageColors.TextOn(category));
            }
            y += 92;

            _tagText = UiKit.Label(t, 30, y, 300, 48, "Tag: -", 32, TextAnchor.MiddleLeft, FontStyle.Bold);
            _status = UiKit.Label(t, 330, y, Width - 360, 48, "", 26, TextAnchor.MiddleRight, FontStyle.Normal, new Color(1f, 0.75f, 0.4f));
        }

        private static float Section(Transform t, float y, string title)
        {
            UiKit.Label(t, 30, y, Width - 60, 40, title.ToUpperInvariant(), 24, TextAnchor.LowerLeft, FontStyle.Bold, UiKit.MutedText);
            UiKit.Panel(t, 30, y + 42, Width - 60, 2, UiKit.MutedText);
            return y + 54;
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
            if (_root != null) Destroy(_root);
        }
    }
}
