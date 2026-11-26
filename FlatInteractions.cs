using BezbroGames.Scamper.Core;
using BezbroGames.Scamper.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlatRatScamper;

internal sealed class FlatInteractions
{
    private const float ClimbSeconds = 3f;
    private const float TvReach = 1.35f;
    private TvFocus focus;
    private ButtonPress press;
    private List<TvScreen> monitors = new();
    private float nextMonitorSearch;
    private Transform climbing;
    private float climbStarted;
    private string prompt, status;
    private float statusUntil;
    private GUIStyle labelStyle;

    internal bool Focused => focus != null;
    internal bool Typing => focus?.IsTyping == true;
    internal bool Climbing => climbing != null;

    internal void KeepFocusedScreenRendering() => focus?.KeepRendering();

    internal void UpdateFocus(Keyboard keyboard, Mouse mouse)
    {
        try
        {
            if (focus != null && !focus.Update(keyboard, mouse))
            {
                ExitFocus();
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("TV input failed; returning to the gameplay view: " + ex);
            ExitFocus();
            ShowStatus("TV input closed");
        }
    }

    internal void UpdateAim(PlayerGlobals globals, Player player, Camera eye, Keyboard keyboard)
    {
        press = press?.Advance() == true ? press : null;
        prompt = null;
        float scale = FlatPhysics.Scale(player);
        Ray aim = eye.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float maxReach = 1.8f * scale;
        float tvReach = Mathf.Min(TvReach * scale, 1.55f);
        RaycastHit? visible = ClosestVisibleHit(aim, player, maxReach);
        if (TryFindTv(aim, visible, tvReach, scale, out var monitor, out var screenHit))
        {
            CancelClimb();
            prompt = "E  Interact with TV";
            if (keyboard.eKey.wasPressedThisFrame)
            {
                ReleaseButton();
                try { focus = TvFocus.TryOpen(monitor, screenHit, eye, globals, player); }
                catch (Exception ex) { Plugin.Logger.LogWarning("Could not open TV focus: " + ex); }
                if (focus == null) ShowStatus("TV controls unavailable");
            }
            return;
        }

        if (!visible.HasValue || visible.Value.collider == null)
        {
            CancelClimb();
            return;
        }

        var collider = visible.Value.collider;
        var ladder = LadderRoot(collider.transform);
        if (ladder != null && visible.Value.distance <= 1.8f * scale)
        {
            prompt = "Hold E  Climb";
            UpdateClimb(ladder, eye.transform.position, player, keyboard);
            return;
        }

        CancelClimb();
        if (visible.Value.distance > 1.15f * scale)
        {
            return;
        }

        var interaction = ButtonPress.Find(collider);
        if (interaction != null)
        {
            prompt = "E  Press button";
            if (keyboard.eKey.wasPressedThisFrame && press == null)
            {
                press = interaction;
                press.Begin();
            }
        }
    }

    private static RaycastHit? ClosestVisibleHit(Ray ray, Player player, float maxReach)
    {
        RaycastHit? nearest = null;
        foreach (var hit in Physics.RaycastAll(ray, maxReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (hit.collider != null && (!hit.collider.isTrigger || ButtonPress.IsButtonTrigger(hit.collider)) &&
                !FlatPhysics.OwnCollider(player, hit.collider) &&
                (!nearest.HasValue || hit.distance < nearest.Value.distance))
            {
                nearest = hit;
            }
        }
        return nearest;
    }

    private bool TryFindTv(Ray ray, RaycastHit? front, float maxReach, float scale,
        out TvScreen monitor, out RaycastHit surfaceHit)
    {
        if (Time.unscaledTime >= nextMonitorSearch)
        {
            nextMonitorSearch = Time.unscaledTime + 0.7f;
            monitors = TvScreen.FindActiveScreens();
        }

        monitor = null;
        surfaceHit = default;
        foreach (var screen in monitors)
        {
            if (!screen.Active || screen.Mesh == null || !screen.Mesh.Raycast(ray, out var hit, maxReach))
            {
                continue;
            }
            if (front.HasValue && front.Value.distance + 0.025f * scale < hit.distance &&
                !screen.Contains(front.Value.collider))
            {
                continue;
            }
            monitor = screen;
            surfaceHit = hit;
            maxReach = hit.distance;
        }
        return monitor != null;
    }

    private static Transform LadderRoot(Transform hit)
    {
        Transform result = null;
        Transform node = hit;
        for (int depth = 0; node != null && depth < 6; depth++, node = node.parent)
        {
            string name = node.name;
            if (name.Contains("ladder", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("Ladders", StringComparison.OrdinalIgnoreCase))
            {
                result = node;
            }
        }
        return result;
    }

    private void UpdateClimb(Transform ladder, Vector3 approach, Player player, Keyboard keyboard)
    {
        if (!keyboard.eKey.isPressed || Time.timeScale <= 0f || player.disablePhysics || player.playerRigidBody == null ||
            player.playerRigidBody.isKinematic)
        {
            CancelClimb();
            return;
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            climbing = ladder;
            climbStarted = Time.unscaledTime;
        }
        if (climbing != ladder)
        {
            CancelClimb();
            return;
        }
        if (Time.unscaledTime - climbStarted < ClimbSeconds)
        {
            return;
        }

        CancelClimb();
        if (FlatPhysics.TryLadderLanding(player, ladder, approach, out var offset))
        {
            FlatPhysics.TeleportToLadderLanding(player, offset);
        }
        else
        {
            ShowStatus("No clear landing at the top");
        }
    }

    internal void Interrupt()
    {
        ExitFocus();
        ReleaseButton();
        CancelClimb();
        prompt = null;
    }

    internal void ExitFocus()
    {
        var opened = focus;
        focus = null;
        try { opened?.Close(); }
        catch (Exception ex) { Plugin.Logger.LogWarning("TV focus cleanup failed: " + ex); }
    }

    private void ReleaseButton()
    {
        var pressed = press;
        press = null;
        pressed?.Release();
    }

    private void CancelClimb() => climbing = null;

    internal void ShowStatus(string message)
    {
        status = message;
        statusUntil = Time.unscaledTime + 2f;
    }

    internal void DrawHud(float? stamina)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        float x = Screen.width * 0.5f;
        float y = Screen.height * 0.5f;
        labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 19 };
        if (Focused)
        {
            DrawLabel(new Rect(x - 180f, Screen.height - 55f, 360f, 32f), Typing ? "Esc  Exit TV" : "E or Esc  Exit TV");
            return;
        }
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }
        var oldColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(x - 3f, y - 3f, 6f, 6f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x - 1f, y - 1f, 2f, 2f), Texture2D.whiteTexture);
        if (stamina is float fraction)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(x - 19f, y + 7f, 38f, 5f), Texture2D.whiteTexture);
            GUI.color = new Color(0.58f, 0.94f, 0.85f, 0.95f);
            GUI.DrawTexture(new Rect(x - 18f, y + 8f, 36f * fraction, 3f), Texture2D.whiteTexture);
        }
        if (status != null && Time.unscaledTime < statusUntil)
        {
            DrawLabel(new Rect(x - 170f, y + 26f, 340f, 32f), status);
        }
        else if (prompt != null)
        {
            DrawLabel(new Rect(x - 140f, y + (Climbing ? 48f : 24f), 280f, 32f), prompt);
        }
        if (Climbing)
        {
            DrawRing(new Vector2(x, y + 22f), Mathf.Clamp01((Time.unscaledTime - climbStarted) / ClimbSeconds));
        }
        GUI.color = oldColor;
    }

    private void DrawLabel(Rect rect, string value)
    {
        var color = GUI.color;
        GUI.color = new Color(0.02f, 0.025f, 0.03f, 0.72f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, value, labelStyle);
        GUI.color = color;
    }

    private static void DrawRing(Vector2 center, float progress)
    {
        const int segments = 48;
        const float radius = 16f;
        Matrix4x4 saved = GUI.matrix;
        for (int i = 0; i < segments; i++)
        {
            GUI.color = i < progress * segments ? Color.white : new Color(0f, 0f, 0f, 0.50f);
            GUI.matrix = saved;
            GUIUtility.RotateAroundPivot(360f * i / segments, center);
            GUI.DrawTexture(new Rect(center.x - 1.3f, center.y - radius, 2.6f, 2.4f), Texture2D.whiteTexture);
        }
        GUI.matrix = saved;
    }
}
