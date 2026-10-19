using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Show;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DataDump.Adapters;

/// <summary>
/// Takes top-down pictures of what the game shows: the camera area the game keeps its view in (CameraManager's
/// min and max world position) and the floor it shows (_appliedVisibleFloor, a MapPoint id). The game draws only that
/// area and that floor, so a picture of anything else comes out black. Each picture goes to
/// maps/&lt;location&gt;/&lt;area&gt;[.floor&lt;id&gt;].&lt;plan|top&gt;.png, beside a .json with the frame that puts the
/// location's node positions on it. On their own, a place's pictures are taken once a session after the view has
/// stayed on it a few seconds; the key retakes the place on view now. A black picture is never written.
/// </summary>
internal sealed class MapCapture
{
    public const int MaxPixels = 4096;

    private const float LookEverySeconds = 1;
    private const int Antialiasing = 8;
    private const int BlankSamples = 32;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _mapsFolder;
    private readonly float _pixelsPerMeter;
    private readonly float _cutHeight;
    private readonly bool _isAuto;
    private readonly CtrlHotkey _key;
    private readonly Action<string> _log;
    private readonly ValueWriter _frames = new(new PlainValueReader(), 4, isIndented: true);
    private readonly AutoViews _auto = new();
    private readonly HashSet<string> _givenUpLogged = new(StringComparer.Ordinal);
    private MapLayout? _layout;
    private float _nextLook;

    public MapCapture(string mapsFolder, float pixelsPerMeter, float cutHeight, bool isAuto, CtrlHotkey key, Action<string> log)
    {
        _mapsFolder = mapsFolder;
        _pixelsPerMeter = pixelsPerMeter;
        _cutHeight = cutHeight;
        _isAuto = isAuto;
        _key = key;
        _log = log;
    }

    public void CheckKey(float now)
    {
        if (!_key.WasPressed()) return;

        if (CurrentView() is not { } view)
        {
            _log($"map pictures ({_key.Name}): no location is on view");

            return;
        }

        var isGood = Take(view, _key.Name);

        _auto.Record(view.Key, isGood, now);
    }

    /// <summary>Looks at what the game shows about once a second and takes its pictures once they are due.</summary>
    public void CheckAuto(float now)
    {
        if (!_isAuto || now < _nextLook) return;

        _nextLook = now + LookEverySeconds;

        var view = CurrentView();
        var key = view?.Key;

        if (view is null || key is null || !_auto.IsDue(key, now)) return;

        _auto.Record(key, Take(view, "on view"), now);

        if (_auto.HasGivenUp(key) && _givenUpLogged.Add(key)) _log($"map pictures of {key} still came out black after {AutoViews.MaxTries} tries; press Ctrl+{_key.Name} there to try again");
    }

    /// <summary>The place the game shows now, or null when no location or camera is there.</summary>
    private MapView? CurrentView()
    {
        if (MapWatcher.Current() is not { } map || WorldCamera() is not { } camera) return null;

        var layout = LayoutOf(map);
        var floorId = camera._appliedFloorGroup > 0 && !camera._appliedAllFloorsVisible ? camera._appliedVisibleFloor : 0;
        var floorPoint = FloorPointOf(floorId);
        var min = PointOf(camera._minWorldPosition_k__BackingField);
        var max = PointOf(camera._maxWorldPosition_k__BackingField);
        var area = MapViews.AreaOf(layout.Areas, min, max)
                   ?? layout.Areas.Find(candidate => candidate.Name == floorPoint?.CameraRange)
                   ?? new MapArea(MapViews.UnknownArea, min, max);
        var cover = MapViews.Cover(area, layout.Areas.Count, layout.Nodes);

        return new MapView(layout.Name, cover, floorId, FloorYOf(layout, floorPoint, cover));
    }

    private static CameraManager? WorldCamera() => GameSingleton.Existing<BattleShowWorld>()?._CameraManager;

    /// <summary>A floor's start point (HomeSecondfloorStart and the like) stands on that floor, so its height is the floor's.</summary>
    private static float FloorYOf(MapLayout layout, Config_MapPoint? floorPoint, MapArea cover)
    {
        var start = floorPoint?.PositionName;

        if (!string.IsNullOrEmpty(start) && layout.Named.TryGetValue(start, out var point)) return point.Y;

        return MapFrame.FloorOf(layout.Nodes, cover.Min, cover.Max, cover.Min.Y);
    }

    /// <summary>The floor's MapPoint row, or null for no floor or a row the game does not have.</summary>
    private Config_MapPoint? FloorPointOf(int floorId)
    {
        if (floorId <= 0) return null;

        try
        {
            return GameSingleton.Existing<ConfigManager>()?.Get_Config_MapPoint(floorId);
        }
        catch (Exception ex)
        {
            _log($"map pictures: no map point {floorId}: {ex.Message}");

            return null;
        }
    }

    private MapLayout LayoutOf(Config_Map map)
    {
        if (_layout is { } layout && layout.Pointer == map.Pointer) return layout;

        _layout = MapLayout.Of(map);

        return _layout;
    }

    /// <summary>Takes and writes a view's pictures; true when none came out black.</summary>
    private bool Take(MapView view, string trigger)
    {
        var shots = MapShots.For(view, _cutHeight, _pixelsPerMeter, MaxPixels);
        var folder = Path.Combine(_mapsFolder, FileNames.Safe(view.Location));
        var mask = CullingMask();
        var isGood = shots.Count > 0;

        foreach (var shot in shots)
        {
            try
            {
                var (png, isBlank) = Render(shot, mask);

                if (isBlank)
                {
                    isGood = false;
                    _log($"map picture {view.Location}/{shot.FileStem} ({trigger}) came out black, not written (layers {mask:X})");

                    continue;
                }

                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, shot.FileStem + ".png"), png);
                File.WriteAllText(Path.Combine(folder, shot.FileStem + ".json"), _frames.Write(FrameDoc.Of(view, shot)) + "\n", Utf8);
                _log($"map picture {view.Location}/{shot.FileStem} ({trigger}): {shot.Frame.Width}x{shot.Frame.Height}, floor {view.FloorId} at {view.FloorY:0.##} m, layers {mask:X}");
            }
            catch (Exception ex)
            {
                isGood = false;
                _log($"map picture {view.Location}/{shot.FileStem} failed: {ex}");
            }
        }

        return isGood;
    }

    /// <summary>
    /// The layers to draw. While a menu is open the game sets its camera's mask to nothing and keeps the real one in
    /// _freezeSavedCullingMask, so a picture copied from that camera would be empty.
    /// </summary>
    private static int CullingMask()
    {
        var mask = Camera.main is { } main ? main.cullingMask : 0;

        if (mask != 0) return mask;

        var saved = WorldCamera()?._freezeSavedCullingMask ?? 0;

        return saved != 0 ? saved : ~0;
    }

    /// <summary>
    /// Renders one picture with a camera of its own, copied from the game's main camera (its render settings) and then
    /// pointed straight down: picture right is world X, picture up is world Z. It draws into an antialiased target,
    /// which is resolved into a plain one to read back.
    /// </summary>
    private static (byte[] Png, bool IsBlank) Render(MapShot shot, int mask)
    {
        var frame = shot.Frame;
        var holder = new GameObject("Data Dump map camera");
        var smooth = new RenderTexture(frame.Width, frame.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = Antialiasing };
        var plain = new RenderTexture(frame.Width, frame.Height, 0, RenderTextureFormat.ARGB32);
        var picture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;

        try
        {
            var camera = holder.AddComponent<Camera>();

            if (Camera.main is { } main) camera.CopyFrom(main);

            camera.enabled = false;
            camera.cullingMask = mask;
            camera.targetTexture = smooth;
            camera.orthographic = true;
            camera.orthographicSize = frame.HalfHeight;
            camera.aspect = (float)frame.Width / frame.Height;
            camera.nearClipPlane = shot.Near;
            camera.farClipPlane = shot.Far;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.SetPositionAndRotation(new Vector3(frame.CenterX, shot.CameraY, frame.CenterZ), Quaternion.Euler(90f, 0f, 0f));
            camera.Render();
            Graphics.Blit(smooth, plain);

            RenderTexture.active = plain;
            picture.ReadPixels(new Rect(0, 0, frame.Width, frame.Height), 0, 0);
            picture.Apply();

            return (ImageConversion.EncodeToPNG(picture), MapViews.IsBlank(Samples(picture, frame.Width, frame.Height)));
        }
        finally
        {
            RenderTexture.active = previous;
            smooth.Release();
            plain.Release();
            Object.Destroy(smooth);
            Object.Destroy(plain);
            Object.Destroy(picture);
            Object.Destroy(holder);
        }
    }

    /// <summary>An even grid of pixels; reading every pixel of a large picture through the game would take seconds.</summary>
    private static IEnumerable<(byte R, byte G, byte B)> Samples(Texture2D picture, int width, int height)
    {
        for (var row = 0; row < BlankSamples; row++)
        {
            for (var column = 0; column < BlankSamples; column++)
            {
                var color = picture.GetPixel((column * 2 + 1) * width / (BlankSamples * 2), (row * 2 + 1) * height / (BlankSamples * 2));

                yield return (ToByte(color.r), ToByte(color.g), ToByte(color.b));
            }
        }
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp((int)Math.Round(channel * 255), 0, 255);

    private static WorldPoint PointOf(Vector3 position) => new(position.x, position.y, position.z);

    /// <summary>What a location's pictures need from its map, read once per location: its camera areas and its nodes.</summary>
    private sealed record MapLayout(IntPtr Pointer, string Name, List<MapArea> Areas, List<WorldPoint> Nodes, Dictionary<string, WorldPoint> Named)
    {
        public static MapLayout Of(Config_Map map)
        {
            var areas = new List<MapArea>();
            var nodes = new List<WorldPoint>();
            var named = new Dictionary<string, WorldPoint>(StringComparer.Ordinal);

            if (map.CameraRanges is { } ranges)
            {
                foreach (var range in ranges)
                {
                    if (range is not null) areas.Add(new MapArea(range.NodeName ?? "area", PointOf(range.MinWorldPosition), PointOf(range.MaxWorldPosition)));
                }
            }

            foreach (var spots in new[] { map.NodeMap, map.BuildingNodeMap, map.PackageNodeMap })
            {
                if (spots is null) continue;

                foreach (var pair in spots)
                {
                    var point = PointOf(pair.Value);

                    nodes.Add(point);

                    if (!string.IsNullOrEmpty(pair.Key)) named.TryAdd(pair.Key, point);
                }
            }

            return new MapLayout(map.Pointer, string.IsNullOrEmpty(map.Name) ? "unnamed" : map.Name, areas, nodes, named);
        }
    }

    /// <summary>The .json beside a picture: what the site needs to put a world position on it.</summary>
    private sealed record FrameDoc(
        string Location,
        string Area,
        int FloorId,
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
        public static FrameDoc Of(MapView view, MapShot shot)
        {
            var frame = shot.Frame;

            return new FrameDoc(view.Location, view.Area.Name, view.FloorId, shot.Mode, frame.Width, frame.Height, frame.PixelsPerMeter, frame.MinX, frame.MaxX, frame.MinZ, frame.MaxZ,
                frame.FloorY, shot.CameraY, shot.Near, shot.Far, "pixel x = (X - MinX) * PixelsPerMeter from the left; pixel y = (MaxZ - Z) * PixelsPerMeter from the top");
        }
    }
}
