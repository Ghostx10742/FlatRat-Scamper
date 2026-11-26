using BezbroGames.Scamper.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FlatRatScamper;

internal sealed class TvScreen
{
    private readonly MonitorScreenBase monitor;
    private readonly PodiumMonitorScreen podium;
    private Camera screenCamera;
    private RectTransform screenCanvas;
    private MonitorScreenBase mirroredMonitor;

    private TvScreen(MonitorScreenBase monitor) => this.monitor = monitor;
    private TvScreen(PodiumMonitorScreen podium) => this.podium = podium;

    private Behaviour Owner => monitor != null ? monitor : podium;
    internal bool Active => Owner != null && Owner.isActiveAndEnabled;
    internal MeshCollider Mesh => monitor != null ? monitor.tvMesh : podium.tvMesh;
    internal Camera RenderCamera => screenCamera;
    internal RectTransform Canvas => screenCanvas;
    internal MainUIHandler MainMenu
    {
        get
        {
            var full = ControlMonitor?.TryCast<MonitorScreen>();
            return full != null ? full.mainUIHandler : null;
        }
    }
    internal Vector2 ScreenSize => monitor != null ? monitor.screen_size : podium.screen_size;
    internal Vector2 RemapX => monitor != null ? monitor.remapUVx : podium.remapUVx;
    internal Vector2 RemapY => monitor != null ? monitor.remapUVy : podium.remapUVy;
    private MonitorScreenBase ControlMonitor => monitor != null ? monitor : mirroredMonitor;

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
            screenCamera = podium.renderCamera != null ? podium.renderCamera : podium.fallbackMonitorCamera;
            screenCanvas = podium.canvasRectTransform != null ? podium.canvasRectTransform : podium.fallbackMonitorCanvas;
            if (screenCamera == null && screenCanvas == null) BindMirroredScreen();
        }
        else
        {
            screenCamera = monitor.renderCamera;
            screenCanvas = monitor.canvasRectTransform;
        }

        if (screenCanvas == null && screenCamera != null)
        {
            var canvas = screenCamera.GetComponentInChildren<Canvas>(true);
            if (canvas != null) screenCanvas = canvas.transform.TryCast<RectTransform>();
        }
        if (screenCamera == null && screenCanvas != null)
        {
            var canvas = screenCanvas.GetComponent<Canvas>();
            if (canvas != null) screenCamera = canvas.worldCamera;
        }
        if (Mesh == null || !Mesh.enabled || screenCamera == null || screenCanvas == null)
        {
            Plugin.Logger.LogWarning($"TV {Owner.name} has incomplete bindings (camera={screenCamera != null}, canvas={screenCanvas != null}).");
            return false;
        }
        ControlMonitor?.RefreshUIControls();
        return true;
    }

    private void BindMirroredScreen()
    {
        // Some map screens display the render texture supplied by another TV.
        // Only use its controls when the displayed texture actually matches.
        var renderer = podium.screenMeshRenderer;
        if (renderer == null && Mesh != null) renderer = Mesh.GetComponent<MeshRenderer>();
        var material = renderer == null ? null : renderer.sharedMaterial;
        if (material == null || !material.HasProperty("_RenderTexture")) return;
        var texture = material.GetTexture("_RenderTexture");
        if (texture == null) return;

        foreach (var candidate in Object.FindObjectsOfType<MonitorScreenBase>())
        {
            if (candidate.renderCamera == null || candidate.renderCamera.targetTexture != texture ||
                candidate.canvasRectTransform == null) continue;
            mirroredMonitor = candidate;
            screenCamera = candidate.renderCamera;
            screenCanvas = candidate.canvasRectTransform;
            return;
        }
        foreach (var candidate in Object.FindObjectsOfType<PodiumMonitorScreen>())
        {
            if (candidate == podium || !candidate.TryRefreshBindings() || candidate.renderCamera == null ||
                candidate.renderCamera.targetTexture != texture || candidate.canvasRectTransform == null) continue;
            screenCamera = candidate.renderCamera;
            screenCanvas = candidate.canvasRectTransform;
            return;
        }
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

    internal bool Allows(Transform target)
    {
        var controls = ControlMonitor;
        return controls == null || controls.IsAllowedByModal(target);
    }

    internal Selectable FindControl(Vector2 point)
    {
        var nativeControls = ControlMonitor;
        if (nativeControls != null)
        {
            return nativeControls.FindTouchedControl(point, point);
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
