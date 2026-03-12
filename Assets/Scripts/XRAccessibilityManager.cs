// XRAccessibilityManager.cs
using UnityEngine;
using UnityEngine.InputSystem;
using System;

/// <summary>
/// Accessibility controller system for a Beat Saber–style rhythm game.
///
/// DOMINANT HAND (Inspector)
///   Right — right hand is active; left is driven by mirroring/flick (default).
///   Left  — left hand is active; right is driven by mirroring/flick.
///   Both  — no accessibility overrides; both TrackedPoseDrivers run normally.
///
/// CONTROL MODES
///   Default       — both TrackedPoseDrivers run normally. No overrides.
///   PositionMirror— non-dominant TrackedPoseDriver is DISABLED; dominant controller
///                   drives non-dominant via radial inversion around the player's
///                   head (camera) position.
///   FlickFixed    — non-dominant TrackedPoseDriver is DISABLED; non-dominant controller
///                   frozen at its last position. Dominant-hand gestures are replicated
///                   (with per-FlickMode axis inversion) to the non-dominant SABER.
///   Swap          — sabers re-parented to opposite controllers; non-dominant driver
///                   DISABLED. Only the dominant controller moves.
///
/// FLICK MODES
///   NoInversion      — dominant hand gesture is copied exactly to non-dominant controller.
///   BothAxisInversion— both X and Y of the gesture are flipped.
///
/// INPUT (assign in Inspector via XRI Default Interactions)
///   gripTriggerAction  Grip         — enter/exit controller Position Inversion (toggle).
///   primaryButton      Face A/X     — tap: toggle Swap saber. Overrides flick mode to NoInversion.
///   secondaryButton    Face B/Y     — tap: cycle flick mode (NoInversion ↔ BothAxisInversion).
///   joystickAction     Thumbstick   — fine-tune non-dominant-controller X/Y offset.
/// </summary>
public class XRAccessibilityManager : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────
    //  Inspector fields
    // ──────────────────────────────────────────────────────────────

    [Header("Dominant Hand")]
    [Tooltip("Right: right hand drives left via mirroring/flick. Left: left hand drives right. Both: no accessibility overrides.")]
    public DominantHand dominantHand = DominantHand.Right;

    [Header("Scene References (required)")]
    public Transform xrOrigin;
    public UnityEngine.InputSystem.XR.TrackedPoseDriver leftTrackedDriver;
    public UnityEngine.InputSystem.XR.TrackedPoseDriver rightTrackedDriver;

    [Header("Saber References (required for Swap mode)")]
    [Tooltip("Left-hand saber root Transform. Must be a child of leftTrackedDriver at scene start.")]
    public Transform leftSaber;
    [Tooltip("Right-hand saber root Transform. Must be a child of rightTrackedDriver at scene start.")]
    public Transform rightSaber;

    [Header("Right Hand Input Actions")]
    [Tooltip("Right grip trigger.")]
    public InputActionProperty gripTriggerAction;
    [Tooltip("Right primary face button (A).")]
    public InputActionProperty primaryButton;
    [Tooltip("Right secondary face button (B).")]
    public InputActionProperty secondaryButton;
    [Tooltip("Right thumbstick Vector2.")]
    public InputActionProperty joystickAction;

    [Header("Left Hand Input Actions")]
    [Tooltip("Left grip trigger.")]
    public InputActionProperty leftGripTriggerAction;
    [Tooltip("Left primary face button (X).")]
    public InputActionProperty leftPrimaryButton;
    [Tooltip("Left secondary face button (Y).")]
    public InputActionProperty leftSecondaryButton;
    [Tooltip("Left thumbstick Vector2.")]
    public InputActionProperty leftJoystickAction;

    // ──────────────────────────────────────────────────────────────
    //  Enums
    // ──────────────────────────────────────────────────────────────

    public enum DominantHand { Right, Left, Both }
    public enum ControlMode { Default, PositionMirror, FlickFixed, Swap }
    public enum FlickMode { NoInversion, BothAxisInversion }

    // ──────────────────────────────────────────────────────────────
    //  Inspector-visible state
    // ──────────────────────────────────────────────────────────────

    [Header("State (read-only in play)")]
    public ControlMode currentMode = ControlMode.Default;
    public FlickMode   flickMode   = FlickMode.NoInversion;

    [Header("Position Mirror Settings")]
    [Tooltip("Reference arm separation (metres). 0.6 m ≈ natural shoulder-width for a healthy player.")]
    public float armOffset    = 0.6f;
    public float armOffsetMin = 0.2f;
    public float armOffsetMax = 1.0f;

    [Header("Joystick Settings")]
    [Tooltip("Metres per second at full joystick deflection.")]
    public float joystickSpeed = 0.5f;

    [Header("Gesture Replication Settings")]
    [Tooltip("Minimum dominant-hand speed (m/s) to replicate movement to the non-dominant controller. Below this, the non-dominant controller stays frozen.")]
    public float flickThreshold = 0.3f;
    [Tooltip("How quickly the gesture offset decays back to zero when the hand slows down. Higher = faster return to frozen base.")]
    public float flickReturnSpeed = 6f;

    // ──────────────────────────────────────────────────────────────
    //  Private state
    // ──────────────────────────────────────────────────────────────

    Camera xrCamera;

    // Resolved at Start() based on dominantHand
    UnityEngine.InputSystem.XR.TrackedPoseDriver dominantDriver;
    UnityEngine.InputSystem.XR.TrackedPoseDriver nonDominantDriver;
    Transform dominantSaber;
    Transform nonDominantSaber;
    InputActionProperty domGrip;
    InputActionProperty domPrimary;
    InputActionProperty domSecondary;
    InputActionProperty domJoystick;

    Vector3 joystickOffset;

    // Mode saved before Swap
    ControlMode modeBeforeSwap = ControlMode.Default;

    Vector3    nonDomSaberOrigLocalPos;
    Quaternion nonDomSaberOrigLocalRot;
    Vector3    domSaberOrigLocalPos;
    Quaternion domSaberOrigLocalRot;
    bool       saberOffsetsStored;
    bool       saberSwapped;

    FlickMode flickModeBeforeSwap = FlickMode.NoInversion;

    Vector3 frozenNonDomWorldPos;

    Vector3 previousDomWorldPos;
    Vector3 flickGestureOffset;

    Vector3 saberGestureBaseLocalPos;

    // Crossover detection
    bool wasCrossed;

    bool actionsEnabled;
    bool debugLogs = true;

    // ──────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ──────────────────────────────────────────────────────────────

    void Start()
    {
        if (leftTrackedDriver == null || rightTrackedDriver == null || xrOrigin == null)
            Debug.LogError("[XRAccessibility] Missing required scene references — assign in Inspector.");

        if (leftSaber == null || rightSaber == null)
            Debug.LogWarning("[XRAccessibility] Saber references not assigned — Swap mode will be disabled.");

        if (dominantHand == DominantHand.Right)
        {
            dominantDriver    = rightTrackedDriver;
            nonDominantDriver = leftTrackedDriver;
            dominantSaber     = rightSaber;
            nonDominantSaber  = leftSaber;
            domGrip           = gripTriggerAction;
            domPrimary        = primaryButton;
            domSecondary      = secondaryButton;
            domJoystick       = joystickAction;
        }
        else if (dominantHand == DominantHand.Left)
        {
            dominantDriver    = leftTrackedDriver;
            nonDominantDriver = rightTrackedDriver;
            dominantSaber     = leftSaber;
            nonDominantSaber  = rightSaber;
            domGrip           = leftGripTriggerAction;
            domPrimary        = leftPrimaryButton;
            domSecondary      = leftSecondaryButton;
            domJoystick       = leftJoystickAction;
        }

        xrCamera = Camera.main;
        if (xrCamera == null)
            Debug.LogWarning("[XRAccessibility] No Camera.main found — mirror origin will fall back to xrOrigin.");

        if (dominantDriver != null)
            previousDomWorldPos = dominantDriver.transform.position;

        if (nonDominantSaber != null)
            saberGestureBaseLocalPos = nonDominantSaber.localPosition;
    }

    void OnEnable()
    {
        EnableActions();
        Application.onBeforeRender += OnBeforeRenderApply;
    }

    void OnDisable()
    {
        if (nonDominantDriver != null) nonDominantDriver.enabled = true;
        DisableActions();
        Application.onBeforeRender -= OnBeforeRenderApply;
    }

    void EnableActions()
    {
        try
        {
            TryEnable(domGrip);
            TryEnable(domPrimary);
            TryEnable(domSecondary);
            TryEnable(domJoystick);
            actionsEnabled = true;
            if (debugLogs) Debug.Log("[XRAccessibility] Actions enabled.");
        }
        catch (Exception e) { Debug.LogWarning("[XRAccessibility] Enable error: " + e.Message); }
    }

    void DisableActions()
    {
        try
        {
            TryDisable(domGrip);
            TryDisable(domPrimary);
            TryDisable(domSecondary);
            TryDisable(domJoystick);
            actionsEnabled = false;
        }
        catch (Exception e) { Debug.LogWarning("[XRAccessibility] Disable error: " + e.Message); }
    }

    void Update()
    {
        if (dominantHand == DominantHand.Both) return;
        if (!ReferencesOk() || !actionsEnabled) return;

        HandleGrip();
        HandlePrimary();
        HandleSecondary();
        HandleJoystick();
        HandleFlick();
        CheckCrossover();
    }

    void LateUpdate()
    {
        if (dominantHand != DominantHand.Both) ApplyMode();
    }

    void OnBeforeRenderApply()
    {
        if (dominantHand != DominantHand.Both) ApplyMode();
    }

    // ──────────────────────────────────────────────────────────────
    //  Grip — toggle Position Inversion. If in Swap, exits Swap first.
    // ──────────────────────────────────────────────────────────────

    void HandleGrip()
    {
        if (!Triggered(domGrip)) return;

        if (debugLogs) Debug.Log($"[XRAccessibility] GRIP pressed (was {currentMode}, dominant={dominantHand}).");

        if (currentMode == ControlMode.Swap)
        {
            ExitSwap();
            EnterPositionMirror();
            return;
        }

        switch (currentMode)
        {
            case ControlMode.Default:
                EnterPositionMirror();
                break;
            case ControlMode.PositionMirror:
                EnterFlickFixed();
                break;
            case ControlMode.FlickFixed:
                EnterPositionMirror();
                break;
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Primary (A/X) — tap: toggle Swap saber
    //  Entering Swap disables the non-dominant controller and overrides flickMode.
    //  Exiting Swap (via primary) restores the mode you were in before.
    // ──────────────────────────────────────────────────────────────

    void HandlePrimary()
    {
        if (!Triggered(domPrimary)) return;

        if (debugLogs) Debug.Log($"[XRAccessibility] PRIMARY pressed (was {currentMode}, flickMode={flickMode}).");

        if (currentMode == ControlMode.Swap)
        {
            ExitSwap();
        }
        else
        {
            modeBeforeSwap     = currentMode;
            flickModeBeforeSwap = flickMode;
            flickMode           = FlickMode.NoInversion;
            EnterSwap();
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Secondary (B/Y) — tap: cycle flick mode (NoInversion ↔ BothAxisInversion)
    //  If in Swap, exits Swap first, then cycles.
    // ──────────────────────────────────────────────────────────────

    void HandleSecondary()
    {
        if (!Triggered(domSecondary)) return;

        if (currentMode == ControlMode.Swap)
            ExitSwap();

        FlickMode prev = flickMode;
        flickMode = flickMode == FlickMode.NoInversion
            ? FlickMode.BothAxisInversion
            : FlickMode.NoInversion;

        if (debugLogs) Debug.Log($"[XRAccessibility] SECONDARY pressed → FlickMode {prev} → {flickMode} (mode={currentMode}).");
    }

    // ──────────────────────────────────────────────────────────────
    //  Joystick — fine-tune offset
    // ──────────────────────────────────────────────────────────────

    void HandleJoystick()
    {
        if (currentMode != ControlMode.PositionMirror && currentMode != ControlMode.FlickFixed) return;
        if (domJoystick.action == null || !domJoystick.action.enabled) return;

        Vector2 joy = domJoystick.action.ReadValue<Vector2>();
        if (joy.sqrMagnitude < 0.01f) return;

        float dt = Time.deltaTime;

        if (currentMode == ControlMode.FlickFixed)
        {
            Vector3 worldDelta = xrOrigin.TransformVector(new Vector3(
                joy.x * joystickSpeed * dt,
                joy.y * joystickSpeed * dt,
                0f));
            frozenNonDomWorldPos += worldDelta;
        }
        else
        {
            joystickOffset.x += joy.x * joystickSpeed * dt;
            joystickOffset.y += joy.y * joystickSpeed * dt;
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Gesture replication — active in FlickFixed mode
    //
    //  When the dominant hand moves faster than flickThreshold, its delta
    //  (axis-inverted per FlickMode) is accumulated into flickGestureOffset.
    //  The gesture offset is applied to the non-dominant SABER's local position,
    //  NOT the controller — the controller stays perfectly frozen.
    //
    //  Below the threshold the saber stays at rest. The accumulated
    //  offset smoothly decays back to zero (flickReturnSpeed), gently
    //  returning the saber to its base position after each cut.
    // ──────────────────────────────────────────────────────────────

    void HandleFlick()
    {
        Vector3 domWorldPos = dominantDriver.transform.position;
        Vector3 domDelta    = domWorldPos - previousDomWorldPos;
        float   speed       = domDelta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        previousDomWorldPos = domWorldPos;

        if (currentMode != ControlMode.FlickFixed)
        {
            flickGestureOffset = Vector3.zero;
            return;
        }

        bool wasZero = flickGestureOffset.sqrMagnitude < 0.000001f;

        if (speed > flickThreshold)
        {
            flickGestureOffset += InvertDelta(domDelta);
            if (wasZero && debugLogs)
                Debug.Log($"[XRAccessibility] Gesture START ({speed:F2} m/s, {flickMode}).");
        }
        else if (!wasZero)
        {
            flickGestureOffset = Vector3.Lerp(flickGestureOffset, Vector3.zero,
                                              flickReturnSpeed * Time.deltaTime);
            if (flickGestureOffset.sqrMagnitude < 0.000001f)
            {
                flickGestureOffset = Vector3.zero;
                if (debugLogs) Debug.Log("[XRAccessibility] Gesture END — returned to frozen base.");
            }
        }
    }

    /// <summary>
    /// Applies per-FlickMode axis inversion to a world-space movement delta.
    /// Converts to xrOrigin-local for body-relative axis semantics, inverts, converts back.
    ///
    /// NoInversion:       delta passed through unchanged.
    /// BothAxisInversion: local X and Y negated.
    /// </summary>
    Vector3 InvertDelta(Vector3 worldDelta)
    {
        Vector3 localDelta = xrOrigin.InverseTransformVector(worldDelta);

        if (flickMode == FlickMode.BothAxisInversion)
        {
            localDelta.x = -localDelta.x;
            localDelta.y = -localDelta.y;
        }

        return xrOrigin.TransformVector(localDelta);
    }

    // ──────────────────────────────────────────────────────────────
    //  Crossover detection
    // ──────────────────────────────────────────────────────────────

    void CheckCrossover()
    {
        if (currentMode != ControlMode.PositionMirror) return;

        Vector3   mirrorOrigin = GetMirrorOriginLocal();
        Transform domParent    = dominantDriver.transform.parent;
        Vector3   domWorld     = domParent.TransformPoint(dominantDriver.transform.localPosition);
        float     domLocalX    = xrOrigin.InverseTransformPoint(domWorld).x;

        bool isCrossed = (dominantHand == DominantHand.Right)
            ? (domLocalX < mirrorOrigin.x)
            : (domLocalX > mirrorOrigin.x);

        if (isCrossed == wasCrossed) return;
        wasCrossed = isCrossed;

        if (debugLogs)
        {
            string nonDomSide = (dominantHand == DominantHand.Right) ? "left" : "right";
            string domSide    = (dominantHand == DominantHand.Right) ? "right" : "left";
            Debug.Log(isCrossed
                ? $"[XRAccessibility] Controllers CROSSED — {nonDomSide} virtual saber now on {domSide} side."
                : "[XRAccessibility] Controllers UNCROSSED — returned to natural sides.");
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Mode transitions
    // ──────────────────────────────────────────────────────────────

    void ResetSaberGesture()
    {
        if (nonDominantSaber != null)
            nonDominantSaber.localPosition = saberGestureBaseLocalPos;
    }

    void EnterPositionMirror()
    {
        ResetSaberGesture();
        nonDominantDriver.enabled = false;
        currentMode    = ControlMode.PositionMirror;
        joystickOffset = Vector3.zero;
        wasCrossed     = false;
        flickGestureOffset  = Vector3.zero;
        previousDomWorldPos = dominantDriver.transform.position;

        if (debugLogs)
        {
            Vector3 origin = GetMirrorOriginLocal();
            Debug.Log($"[XRAccessibility] PositionMirror ON (origin={origin}, dominant={dominantHand}).");
        }
    }

    void EnterFlickFixed()
    {
        frozenNonDomWorldPos = nonDominantDriver.transform.position;
        nonDominantDriver.enabled = false;
        currentMode         = ControlMode.FlickFixed;
        joystickOffset      = Vector3.zero;
        flickGestureOffset  = Vector3.zero;
        wasCrossed          = false;
        previousDomWorldPos = dominantDriver.transform.position;

        if (nonDominantSaber != null)
            saberGestureBaseLocalPos = nonDominantSaber.localPosition;

        if (debugLogs) Debug.Log($"[XRAccessibility] FlickFixed ON — frozen at {frozenNonDomWorldPos} (dominant={dominantHand}).");
    }

    void EnterSwap()
    {
        ResetSaberGesture();
        joystickOffset     = Vector3.zero;
        flickGestureOffset = Vector3.zero;
        nonDominantDriver.enabled = false;

        SwapSabers();
        currentMode = ControlMode.Swap;

        if (debugLogs) Debug.Log($"[XRAccessibility] Swap ON — non-dominant controller disabled, sabers swapped (dominant={dominantHand}).");
    }

    void ExitSwap()
    {
        RestoreSabers();
        flickMode   = flickModeBeforeSwap;
        currentMode = ControlMode.Default;

        if (debugLogs) Debug.Log($"[XRAccessibility] Swap OFF — sabers restored, flickMode={flickMode}.");
    }

    // ──────────────────────────────────────────────────────────────
    //  Saber swap
    // ──────────────────────────────────────────────────────────────

    void StoreSaberOffsets()
    {
        if (saberOffsetsStored || nonDominantSaber == null || dominantSaber == null) return;

        nonDomSaberOrigLocalPos = nonDominantSaber.localPosition;
        nonDomSaberOrigLocalRot = nonDominantSaber.localRotation;
        domSaberOrigLocalPos    = dominantSaber.localPosition;
        domSaberOrigLocalRot    = dominantSaber.localRotation;
        saberOffsetsStored      = true;

        if (debugLogs) Debug.Log("[XRAccessibility] Saber offsets stored.");
    }

    void SwapSabers()
    {
        if (nonDominantSaber == null || dominantSaber == null)
        {
            Debug.LogWarning("[XRAccessibility] Saber references not assigned — cannot swap.");
            return;
        }

        StoreSaberOffsets();

        nonDominantSaber.SetParent(dominantDriver.transform, false);
        nonDominantSaber.localPosition = domSaberOrigLocalPos;
        nonDominantSaber.localRotation = domSaberOrigLocalRot;

        dominantSaber.SetParent(nonDominantDriver.transform, false);
        dominantSaber.localPosition = nonDomSaberOrigLocalPos;
        dominantSaber.localRotation = nonDomSaberOrigLocalRot;
        dominantSaber.gameObject.SetActive(false);

        saberSwapped = true;
        if (debugLogs) Debug.Log("[XRAccessibility] Sabers SWAPPED — dominant saber hidden on non-dominant controller.");
    }

    void RestoreSabers()
    {
        if (!saberSwapped || nonDominantSaber == null || dominantSaber == null) return;

        nonDominantSaber.SetParent(nonDominantDriver.transform, false);
        nonDominantSaber.localPosition = nonDomSaberOrigLocalPos;
        nonDominantSaber.localRotation = nonDomSaberOrigLocalRot;

        dominantSaber.SetParent(dominantDriver.transform, false);
        dominantSaber.localPosition = domSaberOrigLocalPos;
        dominantSaber.localRotation = domSaberOrigLocalRot;
        dominantSaber.gameObject.SetActive(true);

        saberSwapped = false;
        if (debugLogs) Debug.Log("[XRAccessibility] Sabers RESTORED.");
    }

    // ──────────────────────────────────────────────────────────────
    //  Apply mode (LateUpdate + onBeforeRender)
    // ──────────────────────────────────────────────────────────────

    void ApplyMode()
    {
        if (!ReferencesOk()) return;

        switch (currentMode)
        {
            case ControlMode.PositionMirror:
                ApplyPositionMirror();
                break;
            case ControlMode.FlickFixed:
                ApplyFlickFixed();
                break;
            // Default and Swap: non-dominant controller is frozen — do nothing
        }
    }

    void ApplyPositionMirror()
    {
        Vector3    mirroredPos = MirrorPositionByAxes(dominantDriver.transform.localPosition);
        Quaternion mirroredRot = MirrorRotation(dominantDriver.transform.localRotation);

        if (joystickOffset.sqrMagnitude > 0f)
        {
            Transform nonDomParent = nonDominantDriver.transform.parent;
            mirroredPos += nonDomParent.InverseTransformVector(xrOrigin.TransformVector(joystickOffset));
        }

        nonDominantDriver.transform.localPosition = mirroredPos;
        nonDominantDriver.transform.localRotation = mirroredRot;
    }

    void ApplyFlickFixed()
    {
        Transform  nonDomParent = nonDominantDriver.transform.parent;
        Quaternion domLocalRot  = dominantDriver.transform.localRotation;

        nonDominantDriver.transform.localPosition = nonDomParent.InverseTransformPoint(frozenNonDomWorldPos);

        nonDominantDriver.transform.localRotation = flickMode == FlickMode.BothAxisInversion
            ? InvertMirrorRotation(domLocalRot)
            : MirrorRotation(domLocalRot);

        if (nonDominantSaber != null)
        {
            if (flickGestureOffset.sqrMagnitude > 0.000001f)
            {
                Vector3 localOffset = nonDominantDriver.transform.InverseTransformVector(flickGestureOffset);
                nonDominantSaber.localPosition = saberGestureBaseLocalPos + localOffset;
            }
            else
            {
                nonDominantSaber.localPosition = saberGestureBaseLocalPos;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Spatial math
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the radial mirror origin in xrOrigin-local space.
    /// Uses the XR camera (head) position so the mirror pivots around the
    /// player's natural centre. Falls back to xrOrigin centre if no camera is available.
    /// </summary>
    Vector3 GetMirrorOriginLocal()
    {
        if (xrCamera != null)
            return xrOrigin.InverseTransformPoint(xrCamera.transform.position);
        return Vector3.zero;
    }

    /// <summary>
    /// Radial inversion around the camera/head position in xrOrigin-local space.
    /// Mirrors the dominant controller's position to produce the non-dominant position.
    /// Z is always copied unchanged.
    /// </summary>
    Vector3 MirrorPositionByAxes(Vector3 domLocalPos)
    {
        Transform domParent    = dominantDriver.transform.parent;
        Transform nonDomParent = nonDominantDriver.transform.parent;

        Vector3 worldPos    = domParent.TransformPoint(domLocalPos);
        Vector3 originLocal = xrOrigin.InverseTransformPoint(worldPos);
        Vector3 mirror      = GetMirrorOriginLocal();

        float mx = 2f * mirror.x - originLocal.x;
        float my = 2f * mirror.y - originLocal.y;
        float mz = originLocal.z;

        Vector3 mirroredWorld = xrOrigin.TransformPoint(new Vector3(mx, my, mz));
        return nonDomParent.InverseTransformPoint(mirroredWorld);
    }

    /// <summary>Copies dominant-hand world rotation into non-dominant-hand local space.</summary>
    Quaternion MirrorRotation(Quaternion domLocalRot)
    {
        Transform  domParent    = dominantDriver.transform.parent;
        Transform  nonDomParent = nonDominantDriver.transform.parent;
        Quaternion domWorldRot  = domParent.rotation * domLocalRot;
        return Quaternion.Inverse(nonDomParent.rotation) * domWorldRot;
    }

    /// <summary>
    /// Like MirrorRotation but with a 180° flip around xrOrigin's forward (Z) axis.
    /// Inverts both X and Y in the player's body-relative frame.
    /// </summary>
    Quaternion InvertMirrorRotation(Quaternion domLocalRot)
    {
        Transform  domParent    = dominantDriver.transform.parent;
        Transform  nonDomParent = nonDominantDriver.transform.parent;
        Quaternion domWorldRot  = domParent.rotation * domLocalRot;

        Quaternion originLocal  = Quaternion.Inverse(xrOrigin.rotation) * domWorldRot;
        Quaternion flipped      = Quaternion.AngleAxis(180f, Vector3.forward) * originLocal;
        Quaternion flippedWorld = xrOrigin.rotation * flipped;

        return Quaternion.Inverse(nonDomParent.rotation) * flippedWorld;
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────

    bool ReferencesOk()
        => leftTrackedDriver != null && rightTrackedDriver != null && xrOrigin != null;

    static void TryEnable(InputActionProperty p)
    {
        if (p.action != null && !p.action.enabled) p.action.Enable();
    }

    static void TryDisable(InputActionProperty p)
    {
        if (p.action != null && p.action.enabled) p.action.Disable();
    }

    static bool Triggered(InputActionProperty p)
        => p.action != null && p.action.enabled && p.action.triggered;

    static bool IsPressed(InputActionProperty p)
        => p.action != null && p.action.enabled && p.action.IsPressed();

    // ──────────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────────

    /// <summary>Cycles: Default → PositionMirror → FlickFixed → PositionMirror → ...</summary>
    public void CycleMode()
    {
        if (dominantHand == DominantHand.Both) return;

        switch (currentMode)
        {
            case ControlMode.Default:
                EnterPositionMirror();
                break;
            case ControlMode.PositionMirror:
                EnterFlickFixed();
                break;
            case ControlMode.FlickFixed:
                EnterPositionMirror();
                break;
            case ControlMode.Swap:
                ExitSwap();
                break;
        }
    }
}