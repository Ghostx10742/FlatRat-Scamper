using BezbroGames.Scamper.Core;
using BezbroGames.Scamper.HandTracking;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;
using LegacyPoseDriver = UnityEngine.SpatialTracking.TrackedPoseDriver;

namespace FlatRatScamper;

public sealed class FlatRatController : MonoBehaviour
{
    internal static FlatRatController Active { get; private set; }

    private readonly List<Behaviour> disabledPoseDrivers = new();
    private readonly List<XRBaseController> disabledControllerTracking = new();
    private PlayerGlobals globals;
    private Player player;
    private Camera eye;
    private Transform leftHand, rightHand;
    private Vector3 eyeRestPosition;
    private Quaternion eyeRestRotation;
    private DesktopHands hands;
    private readonly SprintStamina stamina = new();
    private float pitch, viewYawOffset;
    private bool physical, sprint, jumpQueued, xrStopped, cursorOwned, captureLook;
    private Vector2 walk;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private UniversalAdditionalCameraData eyeRendering;
    private bool previousAllowXr;
    private readonly FlatInteractions interactions = new();
    private bool failed;

    public FlatRatController(IntPtr pointer) : base(pointer) { }

    internal HandLocomotionRole HandRole(HandTrackingFeature hand) =>
        physical && captureLook && Application.isFocused && CanMoveBody && !interactions.Focused &&
        !interactions.Climbing && hands?.IsDriving(hand) == true
            ? HandLocomotionRole.Controller
            : HandLocomotionRole.Unavailable;

    internal bool Owns(Player candidate) => player != null && player == candidate && !failed;

    private bool CanLook => globals != null && player != null && eye != null && !globals.killed && !globals.IsRespawning;
    private bool CanMoveBody => CanLook && !player.IsExternalBodyPositionLocked;

    private void Awake() => Active = this;

    private void Update()
    {
        if (failed)
        {
            return;
        }

        try
        {
            var keyboard = Keyboard.current;
            UpdateDesktop(keyboard);
            bool shiftHeld = !physical && Application.isFocused && keyboard != null &&
                (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            sprint = stamina.Update(shiftHeld, walk.sqrMagnitude > 0.001f, Time.deltaTime);
            if (!captureLook || !Application.isFocused || !CanMoveBody || interactions.Focused || interactions.Climbing)
            {
                hands?.Reset();
            }
            ApplyCursor(Application.isFocused);
        }
        catch (Exception ex)
        {
            HandleFailure(ex);
        }
    }

    [HideFromIl2Cpp]
    internal void HandleFailure(Exception ex)
    {
        if (failed)
        {
            return;
        }
        failed = true;
        Plugin.Logger.LogError("FlatRat-Scamper stopped after a runtime error: " + ex);
        try { Shutdown(); }
        catch (Exception cleanup) { Plugin.Logger.LogWarning("Desktop state cleanup failed: " + cleanup); }
    }

    private void UpdateDesktop(Keyboard keyboard)
    {
        if (interactions.Focused && keyboard != null &&
            (keyboard.escapeKey.wasPressedThisFrame || (keyboard.eKey.wasPressedThisFrame && !interactions.Typing)))
        {
            interactions.ExitFocus();
            ResetInput();
            return;
        }

        StopXr();
        var local = PlayerGlobals.Singleton;
        if (local == null || local.playerScript == null || local.playerCamera == null ||
            local.playerScript.leftHandTransform == null || local.playerScript.rightHandTransform == null)
        {
            if (globals != null)
            {
                ReleaseRig();
            }
            return;
        }

        if (local != globals || local.playerCamera != eye || local.playerScript != player ||
            local.playerScript.leftHandTransform != leftHand || local.playerScript.rightHandTransform != rightHand)
        {
            ReleaseRig();
            BindRig(local);
        }

        var mouse = Mouse.current;
        if (!Application.isFocused || !CanLook)
        {
            ResetInput();
            interactions.Interrupt();
            return;
        }

        if (keyboard == null || mouse == null)
        {
            ResetInput();
            hands.Reset();
            return;
        }

        if (keyboard.f1Key.wasPressedThisFrame || keyboard.f2Key.wasPressedThisFrame)
        {
            SetMovementMode(keyboard.f2Key.wasPressedThisFrame);
        }

        if (interactions.Focused)
        {
            ResetInput();
            interactions.UpdateFocus(keyboard, mouse);
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            captureLook = false;
            ResetInput();
            interactions.Interrupt();
            return;
        }

        if (!captureLook)
        {
            if (!mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }
            captureLook = true;
        }
        if (!hands.ReadDrag(mouse, keyboard, physical)) ReadLook(mouse);
        ApplyTrackedPose();
        interactions.UpdateAim(globals!, player!, eye!, keyboard);
        bool canWalk = !physical && !interactions.Focused && !interactions.Climbing && CanMoveBody && !player.disablePhysics;
        if (canWalk)
        {
            walk = Vector2.ClampMagnitude(new Vector2(
                Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed),
                Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed)), 1f);
            jumpQueued |= keyboard.spaceKey.wasPressedThisFrame;
        }
        else
        {
            ResetInput();
        }
    }

    private void StopXr()
    {
        if (xrStopped)
        {
            return;
        }

        var manager = XRGeneralSettings.Instance?.Manager;
        if (manager == null)
        {
            return;
        }

        manager.automaticLoading = false;
        manager.automaticRunning = false;
        if (manager.activeLoader != null)
        {
            if (!manager.isInitializationComplete)
            {
                return;
            }
            manager.StopSubsystems();
            manager.DeinitializeLoader();
        }
        xrStopped = true;
    }

    private void BindRig(PlayerGlobals local)
    {
        globals = local;
        player = local.playerScript;
        eye = local.playerCamera;
        leftHand = player.leftHandTransform;
        rightHand = player.rightHandTransform;
        var eyeTransform = eye.transform;
        eyeRestPosition = eyeTransform.localPosition;
        if (Mathf.Abs(eyeRestPosition.y) < 0.20f)
        {
            eyeRestPosition.y = Mathf.Clamp(Plugin.StandingEyeHeight.Value, 0.65f, 2.2f);
        }
        eyeRestRotation = eyeTransform.localRotation;
        pitch = viewYawOffset = 0f;
        eyeRendering = eye.GetUniversalAdditionalCameraData();
        previousAllowXr = eyeRendering.allowXRRendering;
        eyeRendering.allowXRRendering = false;
        hands = new DesktopHands(player);

        var avatar = local.player != null ? local.player : player.gameObject;
        foreach (var driver in avatar.GetComponentsInChildren<LegacyPoseDriver>(true))
        {
            DisablePoseDriver(driver);
        }
        foreach (var driver in avatar.GetComponentsInChildren<TrackedPoseDriver>(true))
        {
            DisablePoseDriver(driver);
        }
        foreach (var controller in avatar.GetComponentsInChildren<XRBaseController>(true))
        {
            if (controller.enableInputTracking && DrivesPose(controller.transform))
            {
                disabledControllerTracking.Add(controller);
                controller.enableInputTracking = false;
            }
        }

        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        cursorOwned = true;
        captureLook = true;
        ApplyTrackedPose();
        Plugin.Logger.LogInfo($"Bound desktop input to the local rig and disabled {disabledPoseDrivers.Count} pose driver(s).");
    }

    private void DisablePoseDriver(Behaviour driver)
    {
        if (driver.enabled && DrivesPose(driver.transform))
        {
            disabledPoseDrivers.Add(driver);
            driver.enabled = false;
        }
    }

    private bool DrivesPose(Transform node) =>
        eye!.transform == node || leftHand == node || rightHand == node ||
        eye.transform.IsChildOf(node) || leftHand!.IsChildOf(node) || rightHand!.IsChildOf(node) ||
        hands?.Tracks(node) == true;

    private void SetMovementMode(bool handMovement)
    {
        if (physical == handMovement)
        {
            return;
        }

        physical = handMovement;
        ResetInput();
        hands.Reset();
        player!.ForceReleaseClimbingHands();
        player.ClearHandMovementAnchors();
        ApplyTrackedPose();
        player.ResetSavedTransforms();
        interactions.ShowStatus(physical ? "Hold LMB or RMB to plant a hand" : "Keyboard movement");
    }

    private void ReadLook(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue();
        float sensitivity = Mathf.Clamp(Plugin.MouseSensitivity.Value, 0.01f, 1f);
        float requestedYaw = Mathf.DeltaAngle(0f, viewYawOffset + delta.x * sensitivity);
        if (CanMoveBody && Mathf.Abs(requestedYaw) > 0.0001f)
        {
            // Rotate around the eye without the VR turn action's contact and snap bookkeeping.
            // Any yaw the physics root cannot take this frame still reaches the view immediately.
            var rig = player.transform;
            float before = rig.eulerAngles.y;
            player.RotateRigAroundVerticalAxis(eye.transform.position, requestedYaw);
            requestedYaw -= Mathf.DeltaAngle(before, rig.eulerAngles.y);
        }
        viewYawOffset = Mathf.DeltaAngle(0f, requestedYaw);
        pitch = Mathf.Clamp(pitch - delta.y * sensitivity, -89f, 89f);
    }

    private void ApplyTrackedPose(bool forPhysics = false)
    {
        if (!CanLook || interactions.Focused)
        {
            return;
        }

        Transform head = eye!.transform;
        head.localPosition = eyeRestPosition;
        head.localRotation = eyeRestRotation * Quaternion.Euler(pitch, 0f, 0f);
        if (Mathf.Abs(viewYawOffset) > 0.0001f)
        {
            head.rotation = Quaternion.AngleAxis(viewYawOffset, Vector3.up) * head.rotation;
        }
        if (CanMoveBody)
        {
            hands?.Apply(head, physical, forPhysics);
        }
    }

    internal void BeforeNativePhysics() => ApplyTrackedPose(forPhysics: true);

    internal void AfterNativePhysics()
    {
        if (!CanMoveBody || interactions.Focused || player.disablePhysics)
        {
            jumpQueued = false;
            return;
        }
        if (interactions.Climbing)
        {
            var body = player.playerRigidBody;
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.up * body.linearVelocity.y;
            }
            jumpQueued = false;
            return;
        }
        if (physical)
        {
            return;
        }

        FlatPhysics.Walk(player, walk, sprint, jumpQueued);
        jumpQueued = false;
    }

    private void LateUpdate()
    {
        if (!failed)
        {
            try
            {
                ApplyTrackedPose();
                interactions.KeepFocusedScreenRendering();
                ApplyCursor(Application.isFocused);
            }
            catch (Exception ex) { HandleFailure(ex); }
        }
    }

    private void OnGUI()
    {
        if (!failed && eye != null && Application.isFocused)
        {
            interactions.DrawHud(physical ? null : stamina.Fraction);
        }
    }

    private void ResetInput()
    {
        walk = Vector2.zero;
        sprint = jumpQueued = false;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            ResetInput();
            hands?.Reset();
            interactions.Interrupt();
        }
        ApplyCursor(focused);
    }

    internal void Shutdown()
    {
        ReleaseRig();
        if (Active == this)
        {
            Active = null;
        }
    }

    private void OnDestroy() => Shutdown();

    private void ReleaseRig()
    {
        try { interactions.Interrupt(); }
        finally { RestoreRigState(); }
    }

    private void RestoreRigState()
    {
        ResetInput();
        if (cursorOwned)
        {
            Cursor.lockState = Application.isFocused ? previousCursorLock : CursorLockMode.None;
            Cursor.visible = !Application.isFocused || previousCursorVisible;
            cursorOwned = false;
            captureLook = false;
        }
        if (eyeRendering != null)
        {
            eyeRendering.allowXRRendering = previousAllowXr;
        }
        foreach (var driver in disabledPoseDrivers)
        {
            if (driver != null) driver.enabled = true;
        }
        foreach (var controller in disabledControllerTracking)
        {
            if (controller != null) controller.enableInputTracking = true;
        }
        disabledPoseDrivers.Clear();
        disabledControllerTracking.Clear();
        globals = null;
        player = null;
        eye = null;
        eyeRendering = null;
        leftHand = rightHand = null;
        hands = null;
    }

    private void ApplyCursor(bool focused)
    {
        if (!cursorOwned) return;

        bool looking = captureLook && !interactions.Focused;
        var mode = !focused ? CursorLockMode.None : looking ? CursorLockMode.Locked : CursorLockMode.Confined;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        bool visible = mode != CursorLockMode.Locked;
        if (Cursor.visible != visible) Cursor.visible = visible;
    }

    private static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
}
