extern alias il2cpp;

using BezbroGames.Scamper.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TextInputAction = il2cpp::Il2CppSystem.Action<char>;

namespace FlatRatScamper;

internal sealed class TvKeyboard
{
    private readonly TvScreen screen;
    private readonly EventSystem events;
    private Keyboard device;
    private TextInputAction textInput;
    private float nextBackspace = float.PositiveInfinity;

    internal TvKeyboard(TvScreen screen, EventSystem events)
    {
        this.screen = screen;
        this.events = events;
    }

    internal bool IsTyping => Keypad != null || SelectedField != null;

    private MainUIHandler Keypad
    {
        get
        {
            var ui = screen.MainMenu;
            return ui != null && ui.currentScreenType == MainUIHandler.ScreenType.JoinCode &&
                ui.enteredJoinCodeText != null && ui.enteredJoinCodeText.isActiveAndEnabled &&
                screen.Allows(ui.enteredJoinCodeText.transform) ? ui : null;
        }
    }

    private Selectable SelectedField
    {
        get
        {
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null || screen.Canvas == null || !selected.transform.IsChildOf(screen.Canvas) ||
                !screen.Allows(selected.transform))
            {
                return null;
            }
            var tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused && tmp.IsInteractable() && !tmp.readOnly) return tmp;
            var input = selected.GetComponent<InputField>();
            return input != null && input.isFocused && input.IsInteractable() && !input.readOnly ? input : null;
        }
    }

    internal void Update(Keyboard keyboard)
    {
        if (device != keyboard)
        {
            Close();
            textInput ??= (TextInputAction)(Action<char>)OnTextInput;
            keyboard.add_onTextInput(textInput);
            device = keyboard;
        }

        var backspace = keyboard.backspaceKey;
        float now = Time.unscaledTime;
        if (!backspace.isPressed)
        {
            nextBackspace = float.PositiveInfinity;
        }
        else if (backspace.wasPressedThisFrame || now >= nextBackspace)
        {
            nextBackspace = now + (backspace.wasPressedThisFrame ? 0.4f : 0.05f);
            Edit('\b');
        }
    }

    private void OnTextInput(char character)
    {
        if (!Application.isFocused || !screen.Active || !char.IsLetterOrDigit(character)) return;
        Edit(character);
    }

    private void Edit(char character)
    {
        bool erase = character == '\b';
        var keypad = Keypad;
        if (keypad != null)
        {
            if (erase)
            {
                keypad.EnterKey("DEL");
            }
            else if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                keypad.EnterKey(character.ToString());
            }
            return;
        }

        switch (SelectedField)
        {
            case TMP_InputField tmp:
                if (erase) tmp.Backspace(); else tmp.Append(character);
                tmp.ForceLabelUpdate();
                break;
            case InputField input:
                if (erase) input.Backspace(); else input.Append(character);
                input.ForceLabelUpdate();
                break;
        }
    }

    internal void Close()
    {
        var subscribed = device;
        device = null;
        nextBackspace = float.PositiveInfinity;
        if (subscribed != null) subscribed.remove_onTextInput(textInput);
    }
}
