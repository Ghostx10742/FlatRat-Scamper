using BezbroGames.Scamper.Gameplay;
using HurricaneVR.Framework.Components;
using UnityEngine;

namespace FlatRatScamper;

internal sealed class ButtonPress
{
    private const float PressSeconds = 0.10f;
    private const float HoldSeconds = 0.06f;
    private const float ReleaseSeconds = 0.14f;

    private readonly Behaviour component;
    private readonly Action press, release;
    private readonly Action<float> move;
    private readonly float rest, pushed;
    private float start;
    private bool activated;

    private ButtonPress(Behaviour component, Action press, Action release,
        Action<float> move = null, float rest = 0f, float min = 0f, float max = 0f)
    {
        this.component = component;
        this.press = press;
        this.release = release;
        this.move = move;
        this.rest = rest;
        pushed = Mathf.Abs(rest - min) > Mathf.Abs(rest - max) ? min : max;
    }

    internal static bool IsButtonTrigger(Collider collider) =>
        collider.GetComponentInParent<ScamperButton>() != null ||
        collider.GetComponentInParent<SmallButton>() != null ||
        collider.GetComponentInParent<ElevatorPhysicsButton>() != null ||
        collider.GetComponentInParent<TestingButton>() != null ||
        collider.GetComponentInParent<HVRPhysicsButton>() != null;

    internal static ButtonPress Find(Collider collider)
    {
        var physical = collider.GetComponentInParent<HVRPhysicsButton>();
        var objective = collider.GetComponentInParent<ScamperButton>();
        if (objective != null)
        {
            if (!objective.isActiveAndEnabled || !objective.CanRunLocalUpdateLoop() ||
                objective.ShouldStayLockedFromStartZone() || objective.GetEffectiveTriggeredState())
            {
                return null;
            }
            if (physical == null) physical = objective.physicsButton;
            if (physical != null) return FromPhysicalButton(physical);

            return new ButtonPress(objective, () => objective.Press(), objective.ButtonUp, objective.ApplyVisualPosition,
                objective.GetCurrentClampedLocalZ(), objective.minZ, objective.maxZ);
        }

        var small = collider.GetComponentInParent<SmallButton>();
        if (small != null)
        {
            if (!small.isActiveAndEnabled || !small.clickEnabled) return null;
            if (physical == null) physical = small.hVRPhysicsButton;
            if (physical != null) return FromPhysicalButton(physical);

            return new ButtonPress(small, small.ButtonDown, small.ButtonUp, small.ApplyVisualPosition,
                small.GetCurrentClampedLocalZ(), small.minZ, small.maxZ);
        }

        var elevator = collider.GetComponentInParent<ElevatorPhysicsButton>();
        if (elevator != null)
        {
            return elevator.isActiveAndEnabled && elevator._initialized && elevator._button != null
                ? FromPhysicalButton(elevator._button) : null;
        }

        if (physical != null) return FromPhysicalButton(physical);

        var testing = collider.GetComponentInParent<TestingButton>();
        if (testing != null && testing.isActiveAndEnabled)
        {
            return new ButtonPress(testing, testing.ButtonDown, testing.ButtonUp);
        }

        return null;
    }

    private static ButtonPress FromPhysicalButton(HVRPhysicsButton button)
    {
        if (!button.isActiveAndEnabled || button.IsPressed || button.Rigidbody == null ||
            (button.EndPosition - button.StartPosition).sqrMagnitude <= 0.000001f)
        {
            return null;
        }

        // Sample the authored travel at full depth so the stock switch runs CanPress
        // and dispatches all of its listeners, even when Update straddles physics ticks.
        bool pressed = false;
        return new ButtonPress(button, () =>
        {
            button.FixedUpdate();
            pressed |= button.IsPressed;
        }, null, progress =>
        {
            var body = button.Rigidbody;
            if (body == null) return;
            pressed |= button.IsPressed;
            if (progress > 0f && pressed && !button.IsPressed) return;
            var transform = button.transform;
            Vector3 position = Vector3.Lerp(button.StartPosition, button.EndPosition, progress);
            Transform parent = transform.parent;
            body.position = parent == null ? position : parent.TransformPoint(position);
            transform.localPosition = position;
            if (progress == 0f && button.IsPressed) button.FixedUpdate();
        }, max: 1f);
    }

    internal void Begin() => start = Time.unscaledTime;

    internal bool Advance()
    {
        if (component == null || !component.isActiveAndEnabled)
        {
            return false;
        }
        float elapsed = Time.unscaledTime - start;
        if (!activated)
        {
            move?.Invoke(Mathf.Lerp(rest, pushed, Mathf.Clamp01(elapsed / PressSeconds)));
            if (elapsed >= PressSeconds)
            {
                activated = true;
                start = Time.unscaledTime;
                press();
            }
            return true;
        }
        if (elapsed >= HoldSeconds + ReleaseSeconds)
        {
            Release();
            return false;
        }

        float depth = Mathf.Clamp01(1f - (elapsed - HoldSeconds) / ReleaseSeconds);
        move?.Invoke(Mathf.Lerp(rest, pushed, depth));
        return true;
    }

    internal void Release()
    {
        if (component != null && component.isActiveAndEnabled)
        {
            move?.Invoke(rest);
            if (activated) release?.Invoke();
        }
        activated = false;
    }
}
