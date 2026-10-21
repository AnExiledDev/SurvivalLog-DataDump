using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class HookFailureTests
{
    [Fact]
    public void A_hook_that_could_not_be_placed_is_an_error()
    {
        Assert.True(HookFailure.Is("could not hook GameType.Method, it keeps the game's own behavior: method not found"));
    }

    [Fact]
    public void A_placed_hook_and_other_lines_are_not_errors()
    {
        Assert.False(HookFailure.Is("hooked GameType.Method"));
        Assert.False(HookFailure.Is("could not apply a setting: bad value"));
        Assert.False(HookFailure.Is("Could not hook is only matched in the mod's own lowercase wording"));
    }
}
