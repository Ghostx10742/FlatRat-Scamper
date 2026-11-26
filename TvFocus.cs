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
        try
        {
            var events = EventSystem.current;
            if (events == null || !events.isActiveAndEnabled)
            {
                // Some map monitors use the in-world touch path with no active EventSystem.
                events = view.gameObject.AddComponent<EventSystem>();
            }
            keyboardInput = new TvKeyboard(screen, events);
            pointer = new TvPointer(screen, events);
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

    internal static TvFocus TryOpen(TvScreen screen, RaycastHit screenHit, Camera eye,
        PlayerGlobals globals, Player player)
    {
        if (!screen.Prepare() || screen.Mesh.sharedMesh == null)
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
        float aspect = Mathf.Max(0.5f, (float)Screen.width / Mathf.Max(1, Screen.height));
        float scale = FlatPhysics.Scale(player);
        PlaceView(screen, player, eye.transform.position, screenHit.point, center, normal, up, across, along, aspect, scale,
            out Vector3 viewPosition, out Quaternion rotation, out float fieldOfView);

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
            camera.nearClipPlane = Mathf.Min(eye.nearClipPlane, Mathf.Clamp(0.012f * scale, 0.003f, 0.025f));
            var from = eye.GetUniversalAdditionalCameraData();
            var to = camera.GetUniversalAdditionalCameraData();
            to.renderPostProcessing = from.renderPostProcessing;
            to.antialiasing = from.antialiasing;
            to.volumeLayerMask = from.volumeLayerMask;
            to.allowXRRendering = false;
            camera.transform.SetPositionAndRotation(viewPosition, rotation);
            focusObject.SetActive(true);
            return new TvFocus(screen, eye, camera, globals, player);
        }
        catch
        {
            Object.Destroy(focusObject);
            throw;
        }
    }

    private static void PlaceView(TvScreen screen, Player player, Vector3 eye, Vector3 aimedAt,
        Vector3 center, Vector3 normal, Vector3 up, Vector3 across, Vector3 along, float aspect, float scale,
        out Vector3 position, out Quaternion rotation, out float fieldOfView)
    {
        const float normalFov = 55f;
        const float widestFov = 100f;
        var corners = new[] { center + across + along, center + across - along, center - across + along, center - across - along };
        Vector3 right = Vector3.Cross(up, normal).normalized;
        float halfHeight = Mathf.Abs(Vector3.Dot(across, up)) + Mathf.Abs(Vector3.Dot(along, up));
        float halfWidth = Mathf.Abs(Vector3.Dot(across, right)) + Mathf.Abs(Vector3.Dot(along, right));
        float idealDistance = Mathf.Max(0.14f * scale, 1.12f * Mathf.Max(halfHeight, halfWidth / aspect) /
            Mathf.Tan(normalFov * Mathf.Deg2Rad * 0.5f));

        // Prefer a centered view. In tight rooms progressively use the player's
        // approach angle and widen the lens to fit the screen at the available distance.
        Vector3 approach = (eye - center).normalized;
        float inset = Mathf.Max(0.003f, 0.01f * scale);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector3 direction = Vector3.Lerp(normal, approach, attempt / 3f).normalized;
            float distance = idealDistance;
            foreach (var hit in Physics.RaycastAll(center + direction * inset, direction, idealDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && !FlatPhysics.OwnCollider(player, hit.collider) && !screen.Contains(hit.collider))
                {
                    distance = Mathf.Min(distance, hit.distance + inset - 0.035f * scale);
                }
            }
            if (distance < Mathf.Max(0.025f, 0.055f * scale)) continue;

            position = center + direction * distance;
            rotation = Quaternion.LookRotation(center - position, up);
            float required = ScreenFieldOfView(corners, position, rotation, aspect);
            if (required <= widestFov &&
                screen.Mesh.Raycast(new Ray(position, rotation * Vector3.forward), out _, distance + inset))
            {
                fieldOfView = Mathf.Max(normalFov, required);
                return;
            }
        }

        // The player's aim already passed the reach and occlusion checks.
        // Keep that position when the room offers no clear spot in front.
        position = eye;
        Vector3 toward = (center - eye).normalized;
        if (!screen.Mesh.Raycast(new Ray(eye, toward), out _, Vector3.Distance(eye, center) + inset) ||
            Physics.RaycastAll(eye, toward, Vector3.Distance(eye, center), Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore).Any(hit => hit.collider != null &&
                    !FlatPhysics.OwnCollider(player, hit.collider) && !screen.Contains(hit.collider)))
        {
            toward = aimedAt - eye;
        }
        rotation = Quaternion.LookRotation(toward, up);
        fieldOfView = Mathf.Clamp(ScreenFieldOfView(corners, position, rotation, aspect), normalFov, widestFov);
    }

    private static float ScreenFieldOfView(Vector3[] corners, Vector3 position, Quaternion rotation, float aspect)
    {
        Quaternion inverse = Quaternion.Inverse(rotation);
        float tangent = 0f;
        foreach (var corner in corners)
        {
            Vector3 cameraPoint = inverse * (corner - position);
            if (cameraPoint.z <= 0.001f) return 179f;
            tangent = Mathf.Max(tangent, Mathf.Max(Mathf.Abs(cameraPoint.y), Mathf.Abs(cameraPoint.x) / aspect) / cameraPoint.z);
        }
        return 2f * Mathf.Atan(1.12f * tangent) * Mathf.Rad2Deg;
    }

    internal bool Update(Keyboard keyboard, Mouse mouse)
    {
        if (!screen.Active || screen.Mesh == null || renderCamera == null || view == null || eye == null)
        {
            return false;
        }

        KeepRendering();
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

    internal void KeepRendering()
    {
        if (renderCamera != null && !renderCamera.enabled) renderCamera.enabled = true;
    }

    internal void Close()
    {
        try
        {
            try { keyboardInput?.Close(); }
            finally { pointer?.Close(); }
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

    internal TvPointer(TvScreen screen, EventSystem events)
    {
        this.screen = screen;
        this.events = events;
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
