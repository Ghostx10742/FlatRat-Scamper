using BezbroGames.Scamper.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FlatRatScamper;

internal sealed class TvScreen
{
    private readonly MonitorScreenBase monitor;
    private readonly PodiumMonitorScreen podium;

    private TvScreen(MonitorScreenBase monitor) => this.monitor = monitor;
    private TvScreen(PodiumMonitorScreen podium) => this.podium = podium;

    private Behaviour Owner => monitor != null ? monitor : podium;
    internal bool Active => Owner != null && Owner.isActiveAndEnabled;
    internal MeshCollider Mesh => monitor != null ? monitor.tvMesh : podium.tvMesh;
    internal Camera RenderCamera => monitor != null ? monitor.renderCamera : podium.renderCamera;
    internal RectTransform Canvas => monitor != null ? monitor.canvasRectTransform : podium.canvasRectTransform;
    internal MainUIHandler MainMenu
    {
        get
        {
            var full = monitor == null ? null : monitor.TryCast<MonitorScreen>();
            return full != null ? full.mainUIHandler : null;
        }
    }
    internal Vector2 ScreenSize => monitor != null ? monitor.screen_size : podium.screen_size;
    internal Vector2 RemapX => monitor != null ? monitor.remapUVx : podium.remapUVx;
    internal Vector2 RemapY => monitor != null ? monitor.remapUVy : podium.remapUVy;

    internal static List<TvScreen> FindActiveScreens()
    {
        var screens = new List<TvScreen>();
        // This base type includes the lobby MonitorScreen and the monitors in gameplay.
        foreach (var monitor in Object.FindObjectsOfType<MonitorScreenBase>())
        {
            screens.Add(new TvScreen(monitor));
        }
        foreach (var podium in Object.FindObjectsOfType<PodiumMonitorScreen>())
        {
            screens.Add(new TvScreen(podium));
        }
        return screens;
    }

    internal bool Prepare()
    {
        if (!Active)
        {
            return false;
        }

        if (podium != null)
        {
            podium.TryRefreshBindings();
        }
        if (Mesh == null || !Mesh.enabled || RenderCamera == null || Canvas == null)
        {
            return false;
        }
        if (monitor != null)
        {
            monitor.RefreshUIControls();
        }
        return true;
    }

    internal bool Contains(Collider collider)
    {
        Transform owner = Owner.transform;
        if (collider == Mesh || collider.transform.IsChildOf(owner))
        {
            return true;
        }
        Transform housing = owner.parent;
        if (housing != null && housing.name.Contains("monitor", StringComparison.OrdinalIgnoreCase) &&
            collider.transform.IsChildOf(housing))
        {
            return true;
        }
        var full = monitor == null ? null : monitor.TryCast<MonitorScreen>();
        return full != null && (collider == full.monitor || collider == full.frame);
    }

    internal bool Allows(Transform target) => monitor == null || monitor.IsAllowedByModal(target);

    internal Selectable FindControl(Vector2 point)
    {
        if (monitor != null)
        {
            return monitor.FindTouchedControl(point, point);
        }

        // The podium variant has no common hit-test API. Use its native canvas
        // when a control has an authored rectangle but no raycastable graphic.
        if (Canvas == null || RenderCamera == null)
        {
            return null;
        }
        var controls = Canvas.GetComponentsInChildren<Selectable>();
        for (int i = controls.Length - 1; i >= 0; i--)
        {
            var candidate = controls[i];
            if (candidate != null && candidate.isActiveAndEnabled && candidate.IsInteractable() &&
                candidate.transform.TryCast<RectTransform>() is { } rect &&
                RectTransformUtility.RectangleContainsScreenPoint(rect, point, RenderCamera))
            {
                return candidate;
            }
        }
        return null;
    }
}
