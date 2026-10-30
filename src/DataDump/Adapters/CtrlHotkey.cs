using System;
using DataDump.Core;
using UnityEngine;

namespace DataDump.Adapters;

/// <summary>
/// A Ctrl+key hotkey. Turns itself off if the game has the legacy input API disabled.
/// Each call into Unity's input boxes its answer through interop, so <see cref="ReadFrame"/> asks once per frame
/// whether any key went down, and a hotkey asks about its own key only in that frame. A key set to None is off, and a
/// press while a text box has the keyboard is typing, not a hotkey.
/// </summary>
internal sealed class CtrlHotkey
{
    private static readonly Func<KeyCode, bool> IsCtrlKeyDown = key => Input.GetKeyDown(key) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
    private static readonly Func<bool> IsTyping = OptionalLive.IsTyping;

    private static bool _isAnyKeyDown;
    private static bool _isInputUnavailable;

    private KeyCode _key;
    private readonly Action<string> _log;
    private bool _isDisabled;

    public CtrlHotkey(KeyCode key, Action<string> log)
    {
        _key = key;
        _log = log;
    }

    public string Name => "Ctrl+" + _key;

    /// <summary>The ticker's first check: notes whether any key went down this frame, for every hotkey's check after it.</summary>
    public static void ReadFrame(float now)
    {
        if (_isInputUnavailable) return;

        try
        {
            _isAnyKeyDown = Input.anyKeyDown;
        }
        catch (Exception)
        {
            // Each hotkey reports this itself, on its first read below.
            _isInputUnavailable = true;
            _isAnyKeyDown = true;
        }
    }

    /// <summary>Called on the main thread after each change of the key; the next press is read with it.</summary>
    public void Use(KeyCode key) => _key = key;

    public bool WasPressed()
    {
        if (_isDisabled || !_isAnyKeyDown) return false;

        try
        {
            return KeyGate.Fires(_key, KeyCode.None, IsCtrlKeyDown, IsTyping);
        }
        catch (Exception ex)
        {
            _isDisabled = true;
            _log($"{Name} hotkey unavailable, the game's input does not support it: " + ex.Message);

            return false;
        }
    }
}
