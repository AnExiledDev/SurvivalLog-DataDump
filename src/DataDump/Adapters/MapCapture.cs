using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DataDump.Adapters;

/// <summary>
/// Takes top-down pictures of the current location, one per frame: a plan and a top picture of each of its camera
/// areas (Config_Map.CameraRanges), with an orthographic camera added for the picture and removed after it. Each
/// picture goes to maps/&lt;location&gt;/&lt;area&gt;.&lt;plan|top&gt;.png, beside a .json with the frame that puts the
/// location's node positions on it. Locations are taken on their key, and once per game session on their own a few
/// seconds after they load, when that is on.
/// </summary>
internal sealed class MapCapture
{
    public const int MaxPixels = 4096;

    private const double AutoDelaySeconds = 8;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _mapsFolder;
    private readonly float _pixelsPerMeter;
    private readonly float _cutHeight;
    private readonly bool _isAuto;
    private readonly CtrlHotkey _key;
    private readonly Action<string> _log;
    private readonly ValueWriter _frames = new(new PlainValueReader(), 4, isIndented: true);
    private readonly HashSet<string> _autoTaken = new(StringComparer.Ordinal);
    private readonly Queue<(string Location, MapShot Shot)> _waiting = new();
    private (string Location, double DueAt)? _autoDue;

    public MapCapture(string mapsFolder, float pixelsPerMeter, float cutHeight, bool isAuto, CtrlHotkey key, Action<string> log)
    {
        _mapsFolder = mapsFolder;
        _pixelsPerMeter = pixelsPerMeter;
        _cutHeight = cutHeight;
        _isAuto = isAuto;
        _key = key;
        _log = log;
    }

    /// <summary>The <see cref="MapWatcher"/>'s new-location call: schedules the location's own pictures, once a session.</summary>
    public void OnNewMap(Config_Map map, string location, float now)
    {
        _autoDue = _isAuto && !_autoTaken.Contains(location) ? (location, now + AutoDelaySeconds) : null;
    }

    public void CheckKey(float now)
    {
        if (!_key.WasPressed()) return;

        if (MapWatcher.Current() is not { } map)
        {
            _log($"map pictures ({_key.Name}): no location is loaded");

            return;
        }

        Plan(map, NameOf(map), _key.Name);
    }

    public void CheckAutoDue(float now)
    {
        if (_autoDue is not { } due || now < due.DueAt) return;

        _autoDue = null;

        if (MapWatcher.Current() is not { } map || NameOf(map) != due.Location) return;

        _autoTaken.Add(due.Location);
        Plan(map, due.Location, "location loaded");
    }

    /// <summary>Takes the next waiting picture; one a frame, as each can take a moment.</summary>
    public void Tick(float now)
    {
        if (_waiting.Count == 0) return;

        var (location, shot) = _waiting.Dequeue();

        try
        {
            Take(location, shot);
        }
        catch (Exception ex)
        {
            _log($"map picture {location}/{shot.FileStem} failed: {ex}");
        }

        if (_waiting.Count == 0) _log($"map pictures of {location} done, in {Path.Combine(_mapsFolder, FileNames.Safe(location))}");
    }

    private void Plan(Config_Map map, string location, string trigger)
    {
        var shots = MapShots.For(Areas(map), Nodes(map), _cutHeight, _pixelsPerMeter, MaxPixels);

        foreach (var shot in shots) _waiting.Enqueue((location, shot));

        _log($"map pictures of {location} ({trigger}): {shots.Count} to take");
    }

    private static string NameOf(Config_Map map) => string.IsNullOrEmpty(map.Name) ? "unnamed" : map.Name;

    private static List<MapArea> Areas(Config_Map map)
    {
        var areas = new List<MapArea>();

        if (map.CameraRanges is null) return areas;

        foreach (var range in map.CameraRanges)
        {
            if (range is null) continue;

            areas.Add(new MapArea(range.NodeName ?? "area", PointOf(range.MinWorldPosition), PointOf(range.MaxWorldPosition)));
        }

        return areas;
    }

    /// <summary>Every furniture, building and package spot of the map; their heights tell where each area's floor is.</summary>
    private static List<WorldPoint> Nodes(Config_Map map)
    {
        var nodes = new List<WorldPoint>();

        foreach (var spots in new[] { map.NodeMap, map.BuildingNodeMap, map.PackageNodeMap })
        {
            if (spots is null) continue;

            foreach (var pair in spots) nodes.Add(PointOf(pair.Value));
        }

        return nodes;
    }

    private static WorldPoint PointOf(Vector3 position) => new(position.x, position.y, position.z);

    private void Take(string location, MapShot shot)
    {
        var folder = Path.Combine(_mapsFolder, FileNames.Safe(location));

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, shot.FileStem + ".png"), Render(shot));
        File.WriteAllText(Path.Combine(folder, shot.FileStem + ".json"), _frames.Write(FrameDoc.Of(location, shot)) + "\n", Utf8);
    }

    /// <summary>
    /// Renders one picture with a camera of its own, copied from the game's main camera (its layers and render
    /// settings) and then pointed straight down: picture right is world X, picture up is world Z.
    /// </summary>
    private static byte[] Render(MapShot shot)
    {
        var frame = shot.Frame;
        var holder = new GameObject("Data Dump map camera");
        var target = new RenderTexture(frame.Width, frame.Height, 24, RenderTextureFormat.ARGB32);
        var picture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;

        try
        {
            var camera = holder.AddComponent<Camera>();
            var main = Camera.main;

            if (main != null) camera.CopyFrom(main);

            camera.enabled = false;
            camera.targetTexture = target;
            camera.orthographic = true;
            camera.orthographicSize = frame.HalfHeight;
            camera.aspect = (float)frame.Width / frame.Height;
            camera.nearClipPlane = shot.Near;
            camera.farClipPlane = shot.Far;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.SetPositionAndRotation(new Vector3(frame.CenterX, shot.CameraY, frame.CenterZ), Quaternion.Euler(90f, 0f, 0f));
            camera.Render();

            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, frame.Width, frame.Height), 0, 0);
            picture.Apply();

            return ImageConversion.EncodeToPNG(picture);
        }
        finally
        {
            RenderTexture.active = previous;
            target.Release();
            Object.Destroy(target);
            Object.Destroy(picture);
            Object.Destroy(holder);
        }
    }

    /// <summary>The .json beside a picture: what the site needs to put a world position on it.</summary>
    private sealed record FrameDoc(
        string Location,
        string Area,
        string Mode,
        int Width,
        int Height,
        float PixelsPerMeter,
        float MinX,
        float MaxX,
        float MinZ,
        float MaxZ,
        float FloorY,
        float CameraY,
        float Near,
        float Far,
        string Projection)
    {
        public static FrameDoc Of(string location, MapShot shot)
        {
            var frame = shot.Frame;

            return new FrameDoc(location, shot.Area, shot.Mode, frame.Width, frame.Height, frame.PixelsPerMeter, frame.MinX, frame.MaxX, frame.MinZ, frame.MaxZ,
                frame.FloorY, shot.CameraY, shot.Near, shot.Far, "pixel x = (X - MinX) * PixelsPerMeter from the left; pixel y = (MaxZ - Z) * PixelsPerMeter from the top");
        }
    }
}
