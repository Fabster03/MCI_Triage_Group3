using MciTriage.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MciTriage.Runtime
{
    /// <summary>
    /// Link between one patient in the logic and its visual representation.
    /// Reads the current findings every frame and maps them to the visuals.
    /// Placeholder version: a capsule (standing = can walk, lying = cannot walk),
    /// breathing pulse, skin colour and a coloured triage tag.
    /// When real characters arrive, replace the visual mapping in <see cref="ApplyVisuals"/>
    /// with Animator parameters – the logic side stays the same.
    /// </summary>
    public class PatientAvatar : MonoBehaviour
    {
        [SerializeField] private ScenarioController controller;
        [SerializeField] private PatientData patient;

        [Header("Placeholder visuals")]
        [SerializeField] private Transform body;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Renderer triageTag;

        private static readonly Color Skin = new Color(0.87f, 0.68f, 0.55f);
        private static readonly Color Pale = new Color(0.86f, 0.84f, 0.80f);
        private static readonly Color Cyanotic = new Color(0.52f, 0.58f, 0.72f);

        private const float StandingHeight = 0.85f; // capsule half height (scaled)
        private const float LyingHeight = 0.25f;    // capsule radius (scaled)

        private Material _bodyMaterial;
        private Material _tagMaterial;
        private PatientFindings _initialFindings;
        private Vector3 _baseScale;
        private float _breathPhase;
        private float _standing = 1f; // 1 = upright, 0 = lying (smoothed)
        private Transform _label;
        private PatientPanel _panel;

        public ScenarioController Controller => controller;
        public PatientData Patient => patient;
        public string PatientId => patient.patientId;

        /// <summary>Used by the editor scene builder.</summary>
        public void Configure(ScenarioController scenarioController, PatientData data, Transform bodyTransform,
            Renderer bodyRend, Renderer tagRenderer)
        {
            controller = scenarioController;
            patient = data;
            body = bodyTransform;
            bodyRenderer = bodyRend;
            triageTag = tagRenderer;
        }

        private void Awake()
        {
            _bodyMaterial = bodyRenderer.material; // per-instance copy
            _tagMaterial = triageTag.material;
            _baseScale = body.localScale;
            _initialFindings = patient.initialFindings.ToFindings();
            _standing = _initialFindings.CanWalk ? 1f : 0f;
        }

        private void Start()
        {
            BuildLabel();
            _panel = gameObject.AddComponent<PatientPanel>();
            _panel.Init(this);

            // Grip / select on the body opens the examination menu as well.
            var interactable = body.GetComponent<XRSimpleInteractable>();
            if (interactable != null) interactable.selectEntered.AddListener(_ => OpenPanel());
        }

        public void OpenPanel() => _panel.Open();

        private void Update()
        {
            ApplyVisuals(Time.deltaTime);
            UpdateLabel();
        }

        // ------------------------------------------------------------------ logic -> visuals

        private void ApplyVisuals(float dt)
        {
            var session = controller != null ? controller.Session : null;
            var findings = session != null ? session.GetCurrentFindings(PatientId) : _initialFindings;
            bool airwayOpened = session != null && session.IsAirwayOpened(PatientId);
            bool breathing = findings.IsBreathing || (airwayOpened && findings.BreathesAfterAirwayRepositioning);

            // Pose: standing if able to walk, otherwise lying.
            _standing = Mathf.MoveTowards(_standing, findings.CanWalk ? 1f : 0f, dt * 2f);
            body.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(90f, 0f, _standing));
            body.localPosition = new Vector3(0, Mathf.Lerp(LyingHeight, StandingHeight, _standing), 0);

            // Breathing: chest "pulses" with the respiratory rate.
            float amplitude = 0f;
            if (breathing && findings.RespiratoryRate > 0)
            {
                _breathPhase += dt * findings.RespiratoryRate / 60f * Mathf.PI * 2f;
                amplitude = 0.07f;
            }
            float s = 1f + amplitude * Mathf.Sin(_breathPhase);
            body.localScale = new Vector3(_baseScale.x * s, _baseScale.y, _baseScale.z * s);

            // Skin colour: cyanotic if not breathing, pale if poorly perfused.
            Color target = !breathing ? Cyanotic
                : (!findings.HasRadialPulse || findings.CapillaryRefillSeconds > StartTriageEngine.CapillaryRefillThresholdSeconds) ? Pale
                : Skin;
            _bodyMaterial.color = Color.Lerp(_bodyMaterial.color, target, dt * 1.5f);

            // Triage tag.
            var assigned = session != null ? session.GetAssignedCategory(PatientId) : TriageCategory.None;
            triageTag.enabled = assigned != TriageCategory.None;
            if (triageTag.enabled) _tagMaterial.color = TriageColors.Get(assigned);
        }

        // ------------------------------------------------------------------ floating name label

        private void BuildLabel()
        {
            var canvas = UiKit.CreateWorldCanvas("Label", transform, new Vector2(420, 150), 0.0025f);
            _label = canvas.transform;
            UiKit.Panel(_label, 0, 0, 420, 150, UiKit.PanelColor);
            UiKit.Label(_label, 0, 4, 420, 60, patient.displayName, 38, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Button(_label, 60, 72, 300, 64, "Examine", 32, OpenPanel);
        }

        private void UpdateLabel()
        {
            // Hide the name label while this patient's examination panel is open.
            _label.gameObject.SetActive(!_panel.IsOpen);
            float height = Mathf.Lerp(1.1f, 2.3f, _standing);
            _label.position = transform.position + Vector3.up * height;
            UiKit.FaceCamera(_label);
        }
    }
}
