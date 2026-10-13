using System;
using UnityEngine;

namespace DataDump.Adapters;

/// <summary>A Ctrl+key hotkey. Turns itself off if the game has the legacy input API disabled.</summary>
internal sealed class CtrlHotkey
{
    private readonly KeyCode _key;
    private readonly Action<string> _log;
    private bool _isDisabled;

    public CtrlHotkey(KeyCode key, Action<string> log)
    {
        _key = key;
        _log = log;
    }

    public string Name => "Ctrl+" + _key;

    public bool WasPressed()
    {
        if (_isDisabled) return false;

        try
        {
            var isCtrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            return isCtrlHeld && Input.GetKeyDown(_key);
        }
        catch (Exception ex)
        {
            _isDisabled = true;
            _log($"{Name} hotkey unavailable, the game's input does not support it: " + ex.Message);

            return false;
        }
    }
}
