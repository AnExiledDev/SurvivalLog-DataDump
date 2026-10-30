using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

/// <summary>
/// The Ctrl hotkeys: a key set to None never fires, and a key pressed while a text box has the keyboard is
/// text, so it must not dump the tables, save a snapshot or take map pictures.
/// </summary>
public class KeyGateTests
{
    private enum Key { None, F5 }

    private int _downAsks;
    private int _typingAsks;

    [Fact]
    public void A_set_key_fires_when_pressed_and_nobody_is_typing() => Assert.True(Fires(Key.F5, isDown: true, isTyping: false));

    [Fact]
    public void A_set_key_does_not_fire_while_the_player_types_in_a_text_box() => Assert.False(Fires(Key.F5, isDown: true, isTyping: true));

    [Fact]
    public void A_key_that_is_not_pressed_does_not_fire() => Assert.False(Fires(Key.F5, isDown: false, isTyping: false));

    [Fact]
    public void A_key_set_to_None_never_fires_and_reads_nothing()
    {
        Assert.False(Fires(Key.None, isDown: true, isTyping: false));
        Assert.Equal(0, _downAsks);
        Assert.Equal(0, _typingAsks);
    }

    [Fact]
    public void Typing_is_only_asked_once_the_key_went_down()
    {
        Fires(Key.F5, isDown: false, isTyping: true);

        Assert.Equal(1, _downAsks);
        Assert.Equal(0, _typingAsks);
    }

    private bool Fires(Key key, bool isDown, bool isTyping) =>
        KeyGate.Fires(key, Key.None, _ => { _downAsks++; return isDown; }, () => { _typingAsks++; return isTyping; });
}
