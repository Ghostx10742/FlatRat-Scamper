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
    private Quaternion eyeRestRotation, leftWrist, rightWrist;
    private Vector3 leftOffset, rightOffset;
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

    internal HandLocomotionRole HandRole => physical && !interactions.Focused && !interactions.Climbing
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
            UpdateDesktop();
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

    private void UpdateDesktop()
    {
        var keyboard = Keyboard.current;
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
        ReadLook(mouse);
        ApplyTrackedPose();
        interactions.UpdateAim(globals!, player!, eye!, keyboard);
        bool canWalk = !physical && !interactions.Focused && !interactions.Climbing && CanMoveBody && !player.disablePhysics;
        if (canWalk)
        {
            walk = Vector2.ClampMagnitude(new Vector2(
                Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed),
                Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed)), 1f);
            sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
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

        var yaw = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
        leftWrist = Quaternion.Inverse(yaw) * leftHand.rotation;
        rightWrist = Quaternion.Inverse(yaw) * rightHand.rotation;
        ResetHandOffsets();
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
        eye.transform.IsChildOf(node) || leftHand!.IsChildOf(node) || rightHand!.IsChildOf(node);

    private void SetMovementMode(bool handMovement)
    {
        if (physical == handMovement)
        {
            return;
        }

        physical = handMovement;
        ResetInput();
        ResetHandOffsets();
        player!.ForceReleaseClimbingHands();
        player.ClearHandMovementAnchors();
        ApplyTrackedPose();
        player.ResetSavedTransforms();
        interactions.ShowStatus(physical ? "Physical hand movement" : "Keyboard movement");
    }

    private void ResetHandOffsets()
    {
        leftOffset = new Vector3(-0.20f, -0.29f, 0.37f);
        rightOffset = new Vector3(0.20f, -0.29f, 0.37f);
    }

    private void ReadLook(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue();
        bool left = physical && mouse.leftButton.isPressed;
        bool right = physical && mouse.rightButton.isPressed;
        if (left || right)
        {
            Vector3 movement = new(delta.x * 0.0018f, delta.y * 0.0018f,
                Mathf.Clamp(mouse.scroll.ReadValue().y * 0.00035f, -0.12f, 0.12f));
            if (left) leftOffset = MoveHand(leftOffset, movement);
            if (right) rightOffset = MoveHand(rightOffset, movement);
            return;
        }

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

    private Vector3 MoveHand(Vector3 previous, Vector3 movement)
    {
        Vector3 target = previous + movement;
        target.z = Mathf.Clamp(target.z, -0.34f, 0.85f);
        return Vector3.ClampMagnitude(target, Mathf.Clamp(player!.maxArmLength, 0.45f, 1.15f));
    }

    private void ApplyTrackedPose()
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
        if (!CanMoveBody || leftHand == null || rightHand == null)
        {
            return;
        }
        Quaternion yaw = Quaternion.Euler(0f, player!.transform.eulerAngles.y, 0f);
        Quaternion basis = physical ? yaw : head.rotation;
        float scale = FlatPhysics.Scale(player);
        leftHand!.SetPositionAndRotation(head.position + basis * (leftOffset * scale), basis * leftWrist);
        rightHand!.SetPositionAndRotation(head.position + basis * (rightOffset * scale), basis * rightWrist);
    }

    internal void BeforeNativePhysics() => ApplyTrackedPose();

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
                ApplyCursor(Application.isFocused);
            }
            catch (Exception ex) { HandleFailure(ex); }
        }
    }

    private void OnGUI()
    {
        if (!failed && eye != null && Application.isFocused)
        {
            interactions.DrawHud();
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
