using System;
using System.Collections;
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

        [Header("Character model (optional)")]
        [Tooltip("If set, the body is a rigged character instead of the capsule. Its animation (e.g. getting up) " +
                 "is held on the first frame and plays once when the scenario starts. Capsule pose, breathing " +
                 "scale and skin tint are not applied to character models.")]
        [SerializeField] private Animator characterAnimator;

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
        public bool HasCharacterModel => characterAnimator != null;

        /// <summary>Animator bool that switches between Idle and Walk.</summary>
        public const string WalkingParameter = "Walking";

        [Header("Walk demonstration (\"Ask to walk\")")]
        [SerializeField] private float walkDistance = 2.5f;
        [Tooltip("m/s - adjust until the feet do not slide on the ground.")]
        [SerializeField] private float walkSpeed = 1.55f; // measured from the Mixamo walk (feet speed during ground contact)
        [SerializeField] private float turnSeconds = 0.9f;

        /// <summary>True while the patient demonstrates walking.</summary>
        public bool IsWalking { get; private set; }

        /// <summary>Character model with a walk animation that is already standing (not getting up).</summary>
        public bool CanDemonstrateWalk => HasCharacterModel && !IsWalking && HasWalkParameter() &&
                                          characterAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle");

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

        /// <summary>Used by the editor scene builder for rigged character models.</summary>
        public void ConfigureCharacter(Animator animator) => characterAnimator = animator;

        private void Awake()
        {
            if (!HasCharacterModel) _bodyMaterial = bodyRenderer.material; // per-instance copy
            _tagMaterial = triageTag.material;
            _baseScale = body.localScale;
            _initialFindings = patient.initialFindings.ToFindings();
            _standing = _initialFindings.CanWalk ? 1f : 0f;
            if (HasCharacterModel)
            {
                characterAnimator.applyRootMotion = false;
                characterAnimator.speed = 0f; // hold the first frame until the scenario starts
                _standing = 0f;
            }
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

            if (HasCharacterModel)
            {
                // The animation drives the pose; it starts with the scenario and stops on its last frame.
                characterAnimator.speed = session != null ? 1f : 0f;
                var state = characterAnimator.GetCurrentAnimatorStateInfo(0);
                _standing = state.IsName("StandUp") ? Mathf.Clamp01(state.normalizedTime) : 1f;
            }
            else
            {
                ApplyPlaceholderBody(findings, breathing, dt);
            }

            // Triage tag.
            var assigned = session != null ? session.GetAssignedCategory(PatientId) : TriageCategory.None;
            triageTag.enabled = assigned != TriageCategory.None;
            if (triageTag.enabled) _tagMaterial.color = TriageColors.Get(assigned);
        }

        private void ApplyPlaceholderBody(PatientFindings findings, bool breathing, float dt)
        {
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
        }

        // ------------------------------------------------------------------ walk demonstration

        /// <summary>
        /// "Ask to walk": the patient walks a few steps to the side, turns around, comes back and
        /// faces the trainee again. <paramref name="done"/> is called afterwards.
        /// </summary>
        public void DemonstrateWalk(Action done)
        {
            if (!CanDemonstrateWalk) { done?.Invoke(); return; }
            StartCoroutine(WalkRoutine(done));
        }

        private IEnumerator WalkRoutine(Action done)
        {
            IsWalking = true;
            var startPos = transform.position;
            var startRot = transform.rotation;
            var side = startRot * Vector3.right;

            characterAnimator.SetBool(WalkingParameter, true);
            yield return Turn(Quaternion.LookRotation(side), true);
            yield return Walk(startPos + side * walkDistance);
            yield return Turn(Quaternion.LookRotation(-side), true);
            yield return Walk(startPos);
            characterAnimator.SetBool(WalkingParameter, false);
            yield return Turn(startRot, false);

            transform.SetPositionAndRotation(startPos, startRot);
            IsWalking = false;
            done?.Invoke();
        }

        private IEnumerator Walk(Vector3 target)
        {
            while ((transform.position - target).sqrMagnitude > 0.0004f)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, walkSpeed * Time.deltaTime);
                yield return null;
            }
        }

        private IEnumerator Turn(Quaternion target, bool moving)
        {
            var from = transform.rotation;
            float duration = moving ? turnSeconds : turnSeconds * 0.6f;
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                transform.rotation = Quaternion.Slerp(from, target, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            transform.rotation = target;
        }

        private bool HasWalkParameter()
        {
            foreach (var p in characterAnimator.parameters)
                if (p.name == WalkingParameter) return true;
            return false;
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
            _label.gameObject.SetActive(!_panel.IsOpen && !IsWalking);
            float height = Mathf.Lerp(1.1f, 2.3f, _standing);
            _label.position = transform.position + Vector3.up * height;
            UiKit.FaceCamera(_label);
        }
    }
}
