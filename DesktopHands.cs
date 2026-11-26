using BezbroGames.Scamper.Core;
using BezbroGames.Scamper.HandTracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlatRatScamper;

internal sealed class DesktopHands
{
    private readonly Player player;
    private readonly Hand left, right;

    internal DesktopHands(Player player)
    {
        this.player = player;
        var rig = player.GetComponent<ScamperPlayerIKManager>();
        var yaw = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
        left = new Hand(rig != null ? rig.leftTrackedHand : null, player.leftHandTransform, player.leftController, yaw, -1f);
        right = new Hand(rig != null ? rig.rightTrackedHand : null, player.rightHandTransform, player.rightController, yaw, 1f);
    }

    internal bool ReadDrag(Mouse mouse, Keyboard keyboard, bool physical)
    {
        bool leftDrag = physical && mouse.leftButton.isPressed;
        bool rightDrag = physical && mouse.rightButton.isPressed;
        Vector2 delta = mouse.delta.ReadValue();
        bool heightMode = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        float wheel = Mathf.Clamp(mouse.scroll.ReadValue().y / 120f, -2f, 2f) * 0.09f;
        Vector3 motion = new(delta.x * 0.0035f,
            heightMode ? delta.y * 0.0035f : 0f,
            (heightMode ? 0f : delta.y * 0.0045f) + wheel);

        float reach = Mathf.Clamp(player.maxArmLength * 0.7f, 0.45f, 0.85f);
        left.UpdateDrag(leftDrag, motion, reach);
        right.UpdateDrag(rightDrag, motion, reach);
        return leftDrag || rightDrag;
    }

    internal bool IsDriving(HandTrackingFeature hand) =>
        hand != null && ((left.Dragging && left.Device == hand) || (right.Dragging && right.Device == hand));

    internal bool Tracks(Transform source) => left.Tracks(source) || right.Tracks(source);
    internal void Reset()
    {
        left.Reset();
        right.Reset();
    }

    internal void Apply(Transform eye, bool physical, bool forPhysics)
    {
        float scale = FlatPhysics.Scale(player);
        Quaternion yaw = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
        Quaternion basis = physical ? yaw : eye.rotation;
        var body = player.playerRigidBody;
        var velocity = body != null ? body.linearVelocity : Vector3.zero;
        float stride = physical ? 0f : Mathf.Clamp01(new Vector2(velocity.x, velocity.z).magnitude / (2.4f * scale));
        float swing = Mathf.Sin(Time.time * 10f) * stride;
        var headCollider = physical ? player.headCollider : null;
        Vector3 head = headCollider != null ? headCollider.transform.position : eye.position;
        Apply(left, eye.position, head, basis, yaw, scale, swing, physical, forPhysics);
        Apply(right, eye.position, head, basis, yaw, scale, -swing, physical, forPhysics);
    }

    private void Apply(Hand hand, Vector3 eye, Vector3 head, Quaternion basis, Quaternion yaw, float scale, float swing,
        bool physical, bool forPhysics)
    {
        Quaternion rotation = basis * hand.Wrist;
        if (physical && hand.Dragging)
        {
            Vector3 reachPoint = head + yaw * (new Vector3(hand.Offset.x, 0f, hand.Offset.z) * scale);
            float lift = hand.Offset.y * scale;
            if (FlatPhysics.TryHandFloor(player, head, reachPoint, scale, out var floor))
            {
                hand.WritePose(floor.point + floor.normal * (0.045f * scale) + Vector3.up * lift, rotation);
                if (forPhysics)
                {
                    bool isLeft = hand == left;
                    float castRadius = isLeft ? player.minimumRaycastDistanceLeft : player.minimumRaycastDistanceRight;
                    // The visible wrist clears the surface. The much smaller
                    // native locomotion cast needs its center close to the floor.
                    Vector3 contact = floor.point + floor.normal * (castRadius * 0.35f) + Vector3.up * lift;
                    hand.WriteContact(player, isLeft, contact);
                }
                return;
            }

            float lowered = Mathf.Min(0.62f * scale, player.GetScaleAwareMaxArmLength(Vector3.down) * 0.65f);
            hand.WritePose(reachPoint + Vector3.up * (lift - lowered), rotation);
            return;
        }

        Vector3 offset = hand.Rest + new Vector3(0f, 0.025f * swing, 0.055f * swing);
        Vector3 anchor = eye + yaw * (new Vector3(hand.Rest.x, -0.12f, 0.02f) * scale);
        Vector3 target = FlatPhysics.PlaceRestingHand(player, anchor, eye + basis * (offset * scale), scale);
        hand.WritePose(target, rotation);
    }

    private sealed class Hand
    {
        private readonly Transform tracked, controller, collision;
        private readonly Rigidbody trackedBody, controllerBody;
        private readonly Vector3 controllerOffset;
        private readonly Quaternion controllerRotation;
        private readonly float side;
        internal readonly Vector3 Rest;
        internal readonly Quaternion Wrist;
        internal readonly HandTrackingFeature Device;
        internal Vector3 Offset { get; private set; }
        internal bool Dragging { get; private set; }

        internal Hand(Transform nativeTracked, Transform collision, Component nativeController, Quaternion yaw, float side)
        {
            this.side = side;
            this.collision = collision;
            Device = nativeController == null ? null : nativeController.TryCast<HandTrackingFeature>();
            controller = Device != null ? Device.transform : nativeController != null ? nativeController.transform : null;
            tracked = nativeTracked != null ? nativeTracked : controller != null ? controller : collision;
            trackedBody = tracked.GetComponent<Rigidbody>();
            controllerBody = controller == null || controller == tracked ? null : controller.GetComponent<Rigidbody>();
            if (controller != null && controller != tracked && tracked.IsChildOf(controller))
            {
                controllerOffset = controller.InverseTransformPoint(tracked.position);
                controllerRotation = Quaternion.Inverse(controller.rotation) * tracked.rotation;
            }
            else
            {
                controllerRotation = Quaternion.identity;
            }
            Wrist = Quaternion.Inverse(yaw) * tracked.rotation;
            Rest = new Vector3(0.23f * side, -0.19f, 0.39f);
            Reset();
        }

        internal void Reset()
        {
            Offset = new Vector3(Rest.x, 0f, Rest.z);
            Dragging = false;
        }

        internal bool Tracks(Transform source) => tracked == source || tracked.IsChildOf(source) || controller == source;

        internal void UpdateDrag(bool dragging, Vector3 motion, float reach)
        {
            if (!dragging)
            {
                Reset();
                return;
            }

            Dragging = true;
            Vector3 target = Offset + motion;
            target.x = Mathf.Clamp(target.x * side, -0.24f, reach) * side;
            target.z = Mathf.Clamp(target.z, -0.45f, reach);
            Vector2 planar = Vector2.ClampMagnitude(new Vector2(target.x, target.z), reach);
            Offset = new Vector3(planar.x, Mathf.Clamp(target.y, 0f, 0.85f), planar.y);
        }

        internal void WritePose(Vector3 position, Quaternion rotation)
        {
            if (controller != null && controller != tracked)
            {
                Quaternion rootRotation = rotation * Quaternion.Inverse(controllerRotation);
                Vector3 rootPosition = position - rootRotation * Vector3.Scale(controllerOffset, controller.lossyScale);
                SetPose(controller, controllerBody, rootPosition, rootRotation);
            }
            SetPose(tracked, trackedBody, position, rotation);
            // The ParentConstraint also follows the tracked pose later in the frame.
            // Physics needs it before Player.FixedUpdate runs.
            if (collision != tracked) collision.SetPositionAndRotation(position, rotation);
        }

        internal void WriteContact(Player player, bool isLeft, Vector3 center)
        {
            // CurrentHandPosition adds these offsets before doing the sphere cast.
            Vector3 authoredOffset = isLeft ? player.leftHandOffset : player.rightHandOffset;
            Vector3 nativeCenter = player.PositionWithOffset(collision, authoredOffset) +
                player.GetHandLocomotionWorldOffset(isLeft) + player.GetHandPenetrationWorldOffset(isLeft);
            collision.position += center - nativeCenter;
        }

        private static void SetPose(Transform target, Rigidbody body, Vector3 position, Quaternion rotation)
        {
            target.SetPositionAndRotation(position, rotation);
            if (body != null && body.isKinematic)
            {
                body.position = position;
                body.rotation = rotation;
            }
        }
    }
}
