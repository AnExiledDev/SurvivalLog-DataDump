using System;
using GameCore.HotUpdate.Battle.Show;
using UnityEngine.Playables;
using GameLight = GameCore.HotUpdate.LightType;

namespace DataDump.Adapters;

/// <summary>
/// Turns the game's lights to midday for the length of a picture and back. The game lights a place with one timeline
/// per part of the day (LightManager); its debug call GmSetLightType switches to one and can jump to its end. The
/// lights are put back as they were, timeline time included, so the day carries on. When the game will not switch
/// (its lights are locked outside normal play), the picture is taken in the light there is.
/// </summary>
internal sealed class Daylight
{
    private const GameLight Midday = GameLight.Morning2Midday;

    private readonly bool _isEnabled;
    private readonly Action<string> _log;
    private bool _hasLoggedRefusal;

    public Daylight(bool isEnabled, Action<string> log)
    {
        _isEnabled = isEnabled;
        _log = log;
    }

    /// <summary>Runs <paramref name="take"/> in midday light, then puts the game's light back.</summary>
    public T During<T>(Func<T> take)
    {
        var saved = _isEnabled ? SwitchToMidday() : null;

        try
        {
            return take();
        }
        finally
        {
            if (saved is { } light) Restore(light);
        }
    }

    private SavedLight? SwitchToMidday()
    {
        try
        {
            if (GameSingleton.Existing<BattleShowWorld>()?._LightManager is not { } lights) return null;

            var type = lights._currentLightType;
            (double Time, bool IsPlaying)? timeline = null;

            if (DirectorOf(lights, type) is { } director) timeline = (director.time, director.state == PlayState.Playing);

            var saved = new SavedLight(lights, type, timeline);

            if (lights.GmSetLightType(Midday, true, out var reason)) return saved;

            LogRefusalOnce(reason);

            return null;
        }
        catch (Exception ex)
        {
            LogRefusalOnce(ex.Message);

            return null;
        }
    }

    private void Restore(SavedLight saved)
    {
        try
        {
            saved.Lights.GmSetLightType(saved.Type, false, out _);

            if (saved.Timeline is not { } timeline || DirectorOf(saved.Lights, saved.Type) is not { } director) return;

            director.time = timeline.Time;
            director.Evaluate();

            if (timeline.IsPlaying && director.state != PlayState.Playing) director.Play();
        }
        catch (Exception ex)
        {
            _log($"map pictures: could not put the game's light back: {ex.Message}");
        }
    }

    private static PlayableDirector? DirectorOf(LightManager lights, GameLight type)
    {
        if (lights._lightDicByRoot is not { } roots || lights._currentRootName is not { } root) return null;

        return roots.TryGetValue(root, out var byType) && byType is not null && byType.TryGetValue(type, out var director) ? director : null;
    }

    private void LogRefusalOnce(string? reason)
    {
        if (_hasLoggedRefusal) return;

        _hasLoggedRefusal = true;
        _log($"map pictures: the game would not switch to daylight ({reason}); pictures use the light there is");
    }

    private sealed record SavedLight(LightManager Lights, GameLight Type, (double Time, bool IsPlaying)? Timeline);
}
