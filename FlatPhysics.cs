using BezbroGames.Scamper.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace FlatRatScamper;

internal static class FlatPhysics
{
    private static Il2CppStructArray<RaycastHit> handHits;

    internal static float Scale(Player player) => Mathf.Clamp(Mathf.Abs(player.transform.lossyScale.y), 0.05f, 6f);

    internal static bool OwnCollider(Player player, Collider collider) =>
        collider != null && (collider.transform.IsChildOf(player.transform) || player.IsPlayerOwnedCollider(collider));

    internal static Vector3 PlaceRestingHand(Player player, Vector3 shoulder, Vector3 target, float scale)
    {
        Vector3 path = target - shoulder;
        float distance = path.magnitude;
        if (distance <= 0.0001f) return target;

        Vector3 direction = path / distance;
        float radius = 0.07f * scale;
        handHits ??= new Il2CppStructArray<RaycastHit>(24);
        int hits = Physics.SphereCastNonAlloc(shoulder, radius, direction, handHits, distance, WorldMask(player),
            QueryTriggerInteraction.Ignore);
        for (int index = 0; index < hits; index++)
        {
            var hit = handHits[index];
            if (hit.collider == null || OwnCollider(player, hit.collider) || hit.distance > distance) continue;
            distance = Mathf.Max(0f, hit.distance - 0.012f * scale);
        }
        return shoulder + direction * distance;
    }

    internal static bool TryHandFloor(Player player, Vector3 head, Vector3 reachPoint, float scale, out RaycastHit floor)
    {
        floor = default;
        float inset = 0.08f * scale;
        Vector3 origin = new(reachPoint.x, head.y + inset, reachPoint.z);
        float drop = player.GetScaleAwareMaxArmLength(Vector3.down);
        handHits ??= new Il2CppStructArray<RaycastHit>(24);
        int hits = Physics.RaycastNonAlloc(origin, Vector3.down, handHits, drop + inset,
            WorldMask(player), QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int index = 0; index < hits; index++)
        {
            var hit = handHits[index];
            if (hit.distance >= nearest || hit.normal.y < 0.55f || hit.collider == null ||
                OwnCollider(player, hit.collider)) continue;
            nearest = hit.distance;
            floor = hit;
        }
        if (nearest == float.PositiveInfinity) return false;

        Vector3 towardFloor = floor.point - head;
        if (towardFloor.magnitude > player.GetScaleAwareMaxArmLength(towardFloor)) return false;

        // A ledge can be under the cursor's hand target while a wall blocks the reach.
        Vector3 path = floor.point + floor.normal * (0.045f * scale) - head;
        hits = Physics.RaycastNonAlloc(head, path.normalized, handHits, path.magnitude,
            WorldMask(player), QueryTriggerInteraction.Ignore);
        for (int index = 0; index < hits; index++)
        {
            var hit = handHits[index];
            if (hit.collider != null && !OwnCollider(player, hit.collider)) return false;
        }
        return true;
    }

    internal static void Walk(Player player, Vector2 movement, bool sprint, bool jump)
    {
        var body = player.playerRigidBody;
        if (body == null || body.isKinematic || player.IsPortalExitVelocityActive() ||
            !TryBodyVolume(player, out var lower, out var upper, out float radius))
        {
            return;
        }

        float scale = Scale(player);
        float stepTime = Time.fixedDeltaTime;
        if (stepTime <= 0f)
        {
            return;
        }

        Quaternion yaw = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
        float speed = Mathf.Clamp(sprint ? Plugin.SprintSpeed.Value : Plugin.WalkSpeed.Value, 0.1f, 7f) * scale;
        Vector3 desired = (yaw * new Vector3(movement.x, 0f, movement.y)) * speed;
        float clearance = 0.025f * scale;
        int mask = WorldMask(player);
        Vector3 step = LimitStep(player, lower, upper, radius, desired * stepTime, clearance, mask);
        Vector3 velocity = body.linearVelocity;
        Vector3 lateral = new(velocity.x, 0f, velocity.z);
        lateral = Vector3.MoveTowards(lateral, step / stepTime, 23f * scale * stepTime);
        // Apply the collision limit again after acceleration so momentum cannot carry the body through a wall.
        lateral = LimitStep(player, lower, upper, radius, lateral * stepTime, clearance, mask) / stepTime;
        float vertical = velocity.y;
        if (jump && vertical <= 0.15f * scale && IsGrounded(player, lower, radius, scale, mask))
        {
            float height = Mathf.Clamp(Plugin.JumpHeight.Value, 0.1f, 1.1f) * scale;
            vertical = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * height);
        }
        body.linearVelocity = new Vector3(lateral.x, vertical, lateral.z);
    }

    private static Vector3 LimitStep(Player player, Vector3 lower, Vector3 upper, float radius, Vector3 step,
        float clearance, int mask)
    {
        if (!Sweep(player, lower, upper, radius, step, clearance, mask, out var hit))
        {
            return step;
        }

        Vector3 forward = step.normalized * Mathf.Max(0f, hit.distance - clearance);
        Vector3 wall = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
        Vector3 slide = Vector3.ProjectOnPlane(step - forward, wall);
        if (Sweep(player, lower + forward, upper + forward, radius, slide, clearance, mask, out var slideHit))
        {
            slide = slide.normalized * Mathf.Max(0f, slideHit.distance - clearance);
        }
        return forward + slide;
    }

    private static bool Sweep(Player player, Vector3 lower, Vector3 upper, float radius, Vector3 step,
        float clearance, int mask, out RaycastHit nearest)
    {
        nearest = default;
        float limit = step.magnitude;
        if (limit < 0.00001f)
        {
            return false;
        }

        bool found = false;
        Vector3 direction = step / limit;
        limit += clearance;
        foreach (var hit in Physics.CapsuleCastAll(lower, upper, Mathf.Max(radius - clearance, radius * 0.85f),
            direction, limit, mask, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance > limit || hit.collider == null || OwnCollider(player, hit.collider) ||
                hit.normal.y > 0.65f || Vector3.Dot(direction, hit.normal) >= -0.05f)
            {
                continue;
            }
            nearest = hit;
            limit = hit.distance;
            found = true;
        }
        return found;
    }

    private static bool IsGrounded(Player player, Vector3 lower, float radius, float scale, int mask)
    {
        float lift = 0.04f * scale;
        foreach (var hit in Physics.SphereCastAll(lower + Vector3.up * lift, radius * 0.82f, Vector3.down,
            radius * 0.18f + 0.10f * scale + lift, mask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.normal.y >= 0.55f && !OwnCollider(player, hit.collider))
            {
                return true;
            }
        }
        return false;
    }

    internal static int WorldMask(Player player)
    {
        int mask = player.locomotionEnabledLayersCOLLIDE.value;
        return mask != 0 ? mask : Physics.DefaultRaycastLayers;
    }

    internal static bool TryBodyVolume(Player player, out Vector3 lower, out Vector3 upper, out float radius)
    {
        if (player.TryGetClimbedBodyVolume(out lower, out upper, out radius) && radius > 0.01f)
        {
            if (lower.y > upper.y) (lower, upper) = (upper, lower);
            return true;
        }

        CapsuleCollider body = player.bodyCollider;
        if (body == null || body.radius <= 0f)
        {
            return false;
        }

        Transform t = body.transform;
        Vector3 axis = body.direction == 0 ? Vector3.right : body.direction == 1 ? Vector3.up : Vector3.forward;
        float axisScale = t.TransformVector(axis).magnitude;
        float radiusScale = body.direction switch
        {
            0 => Mathf.Max(Mathf.Abs(t.lossyScale.y), Mathf.Abs(t.lossyScale.z)),
            1 => Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.z)),
            _ => Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y))
        };
        radius = body.radius * radiusScale;
        Vector3 center = t.TransformPoint(body.center);
        Vector3 half = t.TransformDirection(axis) * Mathf.Max(0f, body.height * axisScale * 0.5f - radius);
        lower = center - half;
        upper = center + half;
        if (lower.y > upper.y) (lower, upper) = (upper, lower);
        return radius > 0.01f;
    }

    internal static bool TryLadderLanding(Player player, Transform ladder, Vector3 approach, out Vector3 offset)
    {
        offset = default;
        if (!TryBodyVolume(player, out var lower, out var upper, out float radius) ||
            player.playerRigidBody == null || !TryLadderBounds(ladder, out var bounds))
        {
            return false;
        }

        float scale = Scale(player);
        Vector3 away = Vector3.ProjectOnPlane(bounds.center - approach, Vector3.up);
        if (away.sqrMagnitude < 0.0001f)
        {
            away = Vector3.ProjectOnPlane(ladder.forward, Vector3.up);
        }
        if (away.sqrMagnitude < 0.0001f)
        {
            away = Vector3.forward;
        }
        away.Normalize();
        Vector3 sideways = Vector3.Cross(Vector3.up, away);
        float sideExtent = ProjectedExtent(bounds.extents, sideways);
        float depth = ProjectedExtent(bounds.extents, away);
        float top = bounds.max.y;
        float footY = lower.y - radius;
        int mask = WorldMask(player);
        Vector3 origin = new(bounds.center.x, top + 0.65f * scale, bounds.center.z);
        foreach (var direction in new[] { away, sideways, -sideways, -away })
        {
            float extent = Mathf.Abs(Vector3.Dot(away, direction)) > 0.5f ? depth : sideExtent;
            foreach (float distance in new[] { 0.12f, 0.36f, 0.65f, 0.95f })
            {
                Vector3 sample = origin + direction * (extent + radius + distance * scale);
                if (TryFindFloor(player, ladder, sample, 2.15f * scale, mask, out var floor) &&
                    floor.point.y > footY + 0.45f * scale)
                {
                    Vector3 shift = new(floor.point.x - lower.x, floor.point.y - footY + 0.05f * scale,
                        floor.point.z - lower.z);
                    if (LandingClear(player, lower, upper, radius, shift, mask))
                    {
                        offset = shift;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static float ProjectedExtent(Vector3 extents, Vector3 direction) =>
        Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y + Mathf.Abs(direction.z) * extents.z;

    private static bool TryLadderBounds(Transform ladder, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (var collider in ladder.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || collider.bounds.size.sqrMagnitude < 0.0001f)
            {
                continue;
            }
            if (found) bounds.Encapsulate(collider.bounds);
            else bounds = collider.bounds;
            found = true;
        }
        return found;
    }

    private static bool TryFindFloor(Player player, Transform ladder, Vector3 origin, float length, int mask,
        out RaycastHit floor)
    {
        floor = default;
        float nearest = length;
        bool found = false;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, length, mask, QueryTriggerInteraction.Ignore))
        {
            var collider = hit.collider;
            if (collider == null || hit.distance >= nearest || hit.normal.y < 0.65f ||
                collider.transform.IsChildOf(ladder) || OwnCollider(player, collider))
            {
                continue;
            }
            nearest = hit.distance;
            floor = hit;
            found = true;
        }
        return found;
    }

    private static bool LandingClear(Player player, Vector3 lower, Vector3 upper, float radius, Vector3 shift, int mask)
    {
        foreach (var collider in Physics.OverlapCapsule(lower + shift, upper + shift, radius, mask,
            QueryTriggerInteraction.Ignore))
        {
            if (!OwnCollider(player, collider)) return false;
        }
        if (player.TryGetClimbedHeadVolume(out var center, out float headRadius) && headRadius > 0.01f)
        {
            foreach (var collider in Physics.OverlapSphere(center + shift, headRadius, mask,
                QueryTriggerInteraction.Ignore))
            {
                if (!OwnCollider(player, collider)) return false;
            }
        }
        return true;
    }

    internal static void TeleportToLadderLanding(Player player, Vector3 shift)
    {
        var body = player.playerRigidBody;
        player.ForceReleaseClimbingHands();
        player.ClearHandMovementAnchors();
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        Vector3 destination = body.position + shift;
        body.position = destination;
        body.transform.position = destination;
        Physics.SyncTransforms();
        player.ResetSavedTransforms();
        var local = ScamperPlayer.LocalPlayer;
        if (local != null && local.IsOwner)
        {
            local.ForceLocalTeleportNetworkSnap();
        }
    }
}
