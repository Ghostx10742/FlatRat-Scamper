extern alias il2cpp;

using BezbroGames.Scamper.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Raycasts = il2cpp::Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>;

namespace FlatRatScamper;

internal sealed class TvFocus
{
    private readonly TvScreen screen;
    private readonly Camera eye, view, renderCamera;
    private readonly bool eyeWasEnabled, renderCameraWasEnabled;
    private readonly Rigidbody body;
    private readonly RigidbodyConstraints previousConstraints;
    private readonly List<Renderer> hidden = new();
    private readonly TvPointer pointer;
    private readonly TvKeyboard keyboardInput;

    internal bool IsTyping => keyboardInput.IsTyping;

    private TvFocus(TvScreen screen, Camera eye, Camera view, PlayerGlobals globals, Player player)
    {
        this.screen = screen;
        this.eye = eye;
        this.view = view;
        eyeWasEnabled = eye.enabled;
        renderCamera = screen.RenderCamera;
        renderCameraWasEnabled = renderCamera.enabled;
        body = player.playerRigidBody;
        previousConstraints = body == null ? default : body.constraints;
        keyboardInput = new TvKeyboard(screen);
        pointer = new TvPointer(screen);
        try
        {
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.constraints |= RigidbodyConstraints.FreezeAll;
            }
            var owner = globals.player != null ? globals.player : player.gameObject;
            foreach (var renderer in owner.GetComponentsInChildren<Renderer>(true))
            {
                HideRenderer(renderer);
            }
            HideRenderer(globals.rat);
            HideRenderer(globals.refrat);
            renderCamera.enabled = true;
            eye.enabled = false;
            view.enabled = true;
        }
        catch
        {
            Close();
            throw;
        }
    }

    private void HideRenderer(Renderer renderer)
    {
        if (renderer != null && !renderer.forceRenderingOff)
        {
            hidden.Add(renderer);
            renderer.forceRenderingOff = true;
        }
    }

    internal static TvFocus TryOpen(TvScreen screen, Camera eye,
        PlayerGlobals globals, Player player)
    {
        if (!screen.Prepare() || EventSystem.current == null || screen.Mesh.sharedMesh == null)
        {
            return null;
        }

        var mesh = screen.Mesh;
        var bounds = mesh.sharedMesh.bounds;
        var surface = mesh.transform;
        Vector3 center = surface.TransformPoint(bounds.center);
        // All shipped TV meshes are flat in collider-local X. Measure the
        // screen in its own plane, independent of the approach ray or world AABB.
        Vector3 across = surface.TransformVector(Vector3.up * bounds.extents.y);
        Vector3 along = surface.TransformVector(Vector3.forward * bounds.extents.z);
        Vector3 normal = Vector3.Cross(across, along).normalized;
        if (normal.sqrMagnitude < 0.1f)
        {
            return null;
        }
        if (Vector3.Dot(normal, eye.transform.position - center) < 0f)
        {
            normal = -normal;
        }
        Vector3 up = Mathf.Abs(Vector3.Dot(Vector3.up, across.normalized)) >=
            Mathf.Abs(Vector3.Dot(Vector3.up, along.normalized)) ? across.normalized : along.normalized;
        if (Vector3.Dot(up, Vector3.up) < 0f)
        {
            up = -up;
        }
        Quaternion rotation = Quaternion.LookRotation(-normal, up);
        Vector3 right = rotation * Vector3.right;
        float halfHeight = Mathf.Abs(Vector3.Dot(across, up)) + Mathf.Abs(Vector3.Dot(along, up));
        float halfWidth = Mathf.Abs(Vector3.Dot(across, right)) + Mathf.Abs(Vector3.Dot(along, right));
        const float fieldOfView = 55f;
        float aspect = Mathf.Max(0.5f, (float)Screen.width / Mathf.Max(1, Screen.height));
        float verticalTangent = Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f);
        float required = 1.20f * Mathf.Max(halfHeight / verticalTangent, halfWidth / (verticalTangent * aspect));
        float scale = FlatPhysics.Scale(player);
        float distance = Mathf.Max(required, 0.19f * scale);
        foreach (var obstruction in Physics.RaycastAll(center + normal * (0.015f * scale), normal, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (obstruction.collider == null || FlatPhysics.OwnCollider(player, obstruction.collider) ||
                screen.Contains(obstruction.collider))
            {
                continue;
            }
            distance = Mathf.Min(distance, obstruction.distance - 0.06f * scale);
        }
        if (distance < required * 0.75f || distance < 0.09f * scale)
        {
            return null;
        }

        var focusObject = new GameObject("FlatRat TV view");
        focusObject.SetActive(false);
        try
        {
            var camera = focusObject.AddComponent<Camera>();
            camera.CopyFrom(eye);
            camera.enabled = false;
            camera.targetTexture = null;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.orthographic = false;
            camera.usePhysicalProperties = false;
            camera.ResetAspect();
            camera.fieldOfView = fieldOfView;
            camera.ResetProjectionMatrix();
            camera.ResetWorldToCameraMatrix();
            camera.ResetCullingMatrix();
            camera.depth = eye.depth + 100f;
            camera.nearClipPlane = Mathf.Min(eye.nearClipPlane, 0.04f * scale);
            var from = eye.GetUniversalAdditionalCameraData();
            var to = camera.GetUniversalAdditionalCameraData();
            to.renderPostProcessing = from.renderPostProcessing;
            to.antialiasing = from.antialiasing;
            to.volumeLayerMask = from.volumeLayerMask;
            to.allowXRRendering = false;
            camera.transform.SetPositionAndRotation(center + normal * distance, rotation);
            focusObject.SetActive(true);
            return new TvFocus(screen, eye, camera, globals, player);
        }
        catch
        {
            Object.Destroy(focusObject);
            throw;
        }
    }

    internal bool Update(Keyboard keyboard, Mouse mouse)
    {
        if (!screen.Active || screen.Mesh == null || renderCamera == null || view == null || eye == null)
        {
            return false;
        }

        if (!renderCamera.enabled)
        {
            renderCamera.enabled = true;
        }
        var ray = view.ScreenPointToRay(mouse.position.ReadValue());
        if (screen.Mesh.Raycast(ray, out var hit, view.farClipPlane))
        {
            pointer.Update(hit.textureCoord, mouse);
        }
        else
        {
            pointer.Update(null, mouse);
        }
        keyboardInput.Update(keyboard);
        return true;
    }

    internal void Close()
    {
        try
        {
            try { keyboardInput.Close(); }
            finally { pointer.Close(); }
        }
        finally
        {
            if (body != null && body.constraints == (previousConstraints | RigidbodyConstraints.FreezeAll))
            {
                body.constraints = previousConstraints;
            }
            foreach (var renderer in hidden)
            {
                if (renderer != null) renderer.forceRenderingOff = false;
            }
            if (renderCamera != null) renderCamera.enabled = renderCameraWasEnabled;
            if (eye != null) eye.enabled = eyeWasEnabled;
            if (view != null)
            {
                view.enabled = false;
                Object.Destroy(view.gameObject);
            }
        }
    }
}

internal sealed class TvPointer
{
    private readonly TvScreen screen;
    private readonly EventSystem events;
    private readonly PointerEventData data;
    private readonly Raycasts results = new();
    // This IL2CPP value type is exposed as a boxed reference by the game interop.
    private readonly RaycastResult noHit = new();
    private readonly BaseInputModule inputModule;
    private readonly bool inputWasEnabled;
    private GameObject hover;
    private bool hasPosition;

    internal TvPointer(TvScreen screen)
    {
        this.screen = screen;
        events = EventSystem.current;
        data = new PointerEventData(events) { pointerId = -1, button = PointerEventData.InputButton.Left };
        inputModule = events.currentInputModule;
        if (inputModule != null)
        {
            inputWasEnabled = inputModule.enabled;
            inputModule.enabled = false;
        }
    }

    internal void Update(Vector2? uv, Mouse mouse)
    {
        GameObject target = null;
        data.pointerCurrentRaycast = noHit;
        data.delta = Vector2.zero;
        var camera = screen.RenderCamera;
        var canvas = screen.Canvas;
        if (uv.HasValue && camera != null && canvas != null)
        {
            var viewport = new Vector3(
                RemapUv(uv.Value.x, screen.RemapX), RemapUv(uv.Value.y, screen.RemapY), 0f);
            Vector2 size = screen.ScreenSize;
            Vector2 point = size.x > 0f && size.y > 0f
                ? new Vector2(viewport.x * size.x, viewport.y * size.y)
                : (Vector2)camera.ViewportToScreenPoint(viewport);
            data.delta = hasPosition ? point - data.position : Vector2.zero;
            data.position = point;
            hasPosition = true;
            results.Clear();
            events.RaycastAll(data, results);
            for (int i = 0; i < results.Count; i++)
            {
                var hit = results[i];
                if (hit.gameObject != null && hit.module != null && hit.module.TryCast<GraphicRaycaster>() != null &&
                    hit.gameObject.transform.IsChildOf(canvas) && screen.Allows(hit.gameObject.transform))
                {
                    target = hit.gameObject;
                    data.pointerCurrentRaycast = hit;
                    break;
                }
            }
            if (target == null || (ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) == null &&
                ExecuteEvents.GetEventHandler<IDragHandler>(target) == null &&
                ExecuteEvents.GetEventHandler<IScrollHandler>(target) == null))
            {
                var nativeControl = screen.FindControl(point);
                if (nativeControl != null && nativeControl.IsInteractable())
                {
                    target = nativeControl.gameObject;
                    var raycaster = canvas.GetComponentInParent<GraphicRaycaster>();
                    data.pointerCurrentRaycast = new RaycastResult
                    {
                        gameObject = target, module = raycaster, screenPosition = point
                    };
                }
            }
        }
        else
        {
            hasPosition = false;
        }

        UpdateHover(target);
        if (mouse.leftButton.wasPressedThisFrame && target != null)
        {
            BeginPress(target);
        }
        if (mouse.leftButton.isPressed && data.pointerDrag != null && hasPosition)
        {
            Drag();
        }
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            Release(target, true);
        }
        data.scrollDelta = mouse.scroll.ReadValue() / 120f;
        if (target != null && data.scrollDelta.sqrMagnitude > 0f)
        {
            ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.scrollHandler);
        }
    }

    private static float RemapUv(float value, Vector2 mapping) =>
        Mathf.Abs(mapping.y) > 0.0001f ? Mathf.Clamp01((value - mapping.x) / mapping.y) : value;

    private void UpdateHover(GameObject target)
    {
        if (target == hover)
        {
            return;
        }
        for (Transform old = hover == null ? null : hover.transform; old != null; old = old.parent)
        {
            if (target != null && target.transform.IsChildOf(old)) break;
            ExecuteEvents.Execute(old.gameObject, data, ExecuteEvents.pointerExitHandler);
        }
        for (Transform current = target == null ? null : target.transform; current != null; current = current.parent)
        {
            if (hover != null && hover.transform.IsChildOf(current)) break;
            ExecuteEvents.Execute(current.gameObject, data, ExecuteEvents.pointerEnterHandler);
        }
        data.pointerEnter = target;
        hover = target;
    }

    private void BeginPress(GameObject target)
    {
        data.eligibleForClick = true;
        data.pressPosition = data.position;
        data.pointerPressRaycast = data.pointerCurrentRaycast;
        data.useDragThreshold = true;
        data.pointerPress = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler)
            ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
        data.rawPointerPress = target;
        data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
        if (data.pointerDrag != null)
        {
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
        }
    }

    private void Drag()
    {
        if (!data.dragging && (!data.useDragThreshold ||
            (data.position - data.pressPosition).sqrMagnitude >= events.pixelDragThreshold * events.pixelDragThreshold))
        {
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
            data.dragging = true;
            if (data.pointerPress != data.pointerDrag)
            {
                if (data.pointerPress != null)
                {
                    ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
                }
                data.eligibleForClick = false;
                data.pointerPress = null;
                data.rawPointerPress = null;
            }
        }
        if (data.dragging)
        {
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
        }
    }

    private void Release(GameObject target, bool click)
    {
        if (data.pointerPress != null)
        {
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            if (click && target != null && data.eligibleForClick &&
                ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) == data.pointerPress)
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
            }
        }
        if (data.dragging && data.pointerDrag != null)
        {
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
        }
        data.eligibleForClick = data.dragging = false;
        data.pointerPress = data.rawPointerPress = data.pointerDrag = null;
    }

    internal void Close()
    {
        try
        {
            if (events != null)
            {
                Release(null, false);
                UpdateHover(null);
            }
        }
        finally
        {
            if (inputModule != null) inputModule.enabled = inputWasEnabled;
        }
    }
}
