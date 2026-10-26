using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// maps/&lt;phase&gt;/&lt;location&gt;/&lt;area&gt;[.floor&lt;id&gt;].&lt;plan|top&gt;.png, beside a .json with the frame that puts the
/// location's node positions on it. On their own, a place's pictures are taken once a session after the view has
/// stayed on it a few seconds; the key retakes the place on view now. A black picture is never written. A building
/// standing at an angle is pictured turned square, and a large picture is rendered in tiles, as one render target that
/// size may be more than the graphics card allows.
/// </summary>
internal sealed class MapCapture
{
    public const int MaxPixels = 8192;

    private const int TileSize = 2048;
    private const float LookEverySeconds = 1;
    private const int Antialiasing = 8;
    private const int BlankSamples = 32;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _mapsFolder;
    private readonly float _pixelsPerMeter;
    private readonly float _cutHeight;
    private readonly bool _isAuto;
    private readonly CtrlHotkey _key;
    private readonly Daylight _daylight;
    private readonly Action<string> _log;
    private readonly SerialWorker _worker;
    private readonly ValueWriter _frames = new(new PlainValueReader(), 4, isIndented: true);
    private readonly AutoViews _auto = new();
    private readonly HashSet<string> _givenUpLogged = new(StringComparer.Ordinal);
    private readonly LastResult<ViewKey, ViewOnScreen> _lastView;
    private MapLayout? _layout;
    private float _nextLook;

    public MapCapture(string mapsFolder, float pixelsPerMeter, float cutHeight, bool isAuto, CtrlHotkey key, Daylight daylight, Action<string> log, SerialWorker worker)
    {
        _mapsFolder = mapsFolder;
        _pixelsPerMeter = pixelsPerMeter;
        _cutHeight = cutHeight;
        _isAuto = isAuto;
        _key = key;
        _daylight = daylight;
        _log = log;
        _worker = worker;
        _lastView = new LastResult<ViewKey, ViewOnScreen>(WorkOutView);
    }

    public void CheckKey(float now)
    {
        if (!_key.WasPressed()) return;

        if (CurrentView() is not { } seen)
        {
            _log($"map pictures ({_key.Name}): no location is on view");

            return;
        }

        var isGood = Take(seen.View, _key.Name);

        _auto.Record(seen.Key, isGood, now);
    }

    /// <summary>Looks at what the game shows about once a second and takes its pictures once they are due.</summary>
    public void CheckAuto(float now)
    {
        if (!_isAuto || now < _nextLook) return;

        _nextLook = now + LookEverySeconds;

        var seen = CurrentView();
        var key = seen?.Key;

        if (seen is null || key is null || !_auto.IsDue(key, now)) return;

        _auto.Record(key, Take(seen.View, "on view"), now);

        if (_auto.HasGivenUp(key) && _givenUpLogged.Add(key)) _log($"map pictures of {key} still came out black after {AutoViews.MaxTries} tries; press {_key.Name} there to try again");
    }

    /// <summary>
    /// The place the game shows now, or null when no location or camera is there. Worked out again only when the
    /// location, the floor or the camera's box has changed; the view mostly stays put for minutes.
    /// </summary>
    private ViewOnScreen? CurrentView()
    {
        if (MapWatcher.Current() is not { } map || WorldCamera() is not { } camera) return null;

        var layout = LayoutOf(map);
        var floorId = camera._appliedFloorGroup > 0 && !camera._appliedAllFloorsVisible ? camera._appliedVisibleFloor : 0;
        var min = PointOf(camera._minWorldPosition_k__BackingField);
        var max = PointOf(camera._maxWorldPosition_k__BackingField);

        return _lastView.Get(new ViewKey(layout, floorId, min, max));
    }

    private ViewOnScreen WorkOutView(ViewKey key)
    {
        var (layout, floorId, min, max) = (key.Layout, key.FloorId, key.Min, key.Max);
        var floorPoint = FloorPointOf(floorId);
        var isGameDefault = MapViews.IsGameDefault(min, max);
        var area = (isGameDefault ? null : MapViews.AreaOf(FloorAreaFirst(layout.Areas, floorPoint?.CameraRange), min, max))
                   ?? layout.Areas.Find(candidate => candidate.Name == floorPoint?.CameraRange)
                   ?? (isGameDefault ? MapViews.AroundNodes(layout.Name, layout.Nodes) : null)
                   ?? new MapArea(MapViews.UnknownArea, min, max);
        var cover = MapViews.Cover(area, layout.Areas.Count, layout.Nodes);
        var view = new MapView(layout.Name, cover, floorId, FloorYOf(layout, floorPoint, cover), MapViews.YawOf(cover, layout.Turns));

        return new ViewOnScreen(view, view.Key);
    }

    /// <summary>
    /// The areas with the floor's own first: the Office's 1F and 2F are the same box, and a tie goes to the first, so
    /// the second floor's picture is named 2F rather than 1F.
    /// </summary>
    private static List<MapArea> FloorAreaFirst(List<MapArea> areas, string? floorArea) =>
        string.IsNullOrEmpty(floorArea) ? areas : areas.OrderBy(area => area.Name == floorArea ? 0 : 1).ToList();

    private static CameraManager? WorldCamera() => GameSingleton.Existing<BattleShowWorld>()?._CameraManager;

    /// <summary>A floor's start point (HomeSecondfloorStart and the like) stands on that floor, so its height is the floor's.</summary>
    private static float FloorYOf(MapLayout layout, Config_MapPoint? floorPoint, MapArea cover)
    {
        var start = floorPoint?.PositionName;

        if (!string.IsNullOrEmpty(start) && layout.Named.TryGetValue(start, out var point)) return point.Y;

        return MapFrame.FloorOf(layout.Nodes, cover.Min, cover.Max, cover.Min.Y);
    }

    /// <summary>
    /// The floor's MapPoint row, or null for no floor or a row the game does not have. Read from the table directly:
    /// Get_Config_MapPoint writes an error to the game's log for an id it does not have.
    /// </summary>
    private Config_MapPoint? FloorPointOf(int floorId)
    {
        if (floorId <= 0) return null;

        try
        {
            var table = GameSingleton.Existing<ConfigManager>()?._Config_MapPoint_Dict;

            return table is not null && table.TryGetValue(floorId, out var row) ? row : null;
        }
        catch (Exception ex)
        {
            _log($"map pictures: no map point {floorId}: {ex.Message}");

            return null;
        }
    }

    /// <summary>Read once per location; a new one also drops the view worked out for the last.</summary>
    private MapLayout LayoutOf(Config_Map map)
    {
        if (_layout is { } layout && layout.Map.Pointer == map.Pointer) return layout;

        _lastView.Clear();
        _layout = MapLayout.Of(map);

        return _layout;
    }

    /// <summary>Takes and writes a view's pictures in daylight; true when none came out black.</summary>
    private bool Take(MapView view, string trigger) => _daylight.During(() => TakeNow(view, trigger));

    private bool TakeNow(MapView view, string trigger)
    {
        var shots = MapShots.For(view, _cutHeight, _pixelsPerMeter, MaxPixels);
        var folder = Path.Combine(_mapsFolder, WorldPhase.FolderOf(MapWatcher.Phase().WorldState), FileNames.Safe(view.Location));
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

                PostWrite(view, shot, folder, png, trigger, mask);
            }
            catch (Exception ex)
            {
                isGood = false;
                _log($"map picture {view.Location}/{shot.FileStem} failed: {ex}");
            }
        }

        return isGood;
    }

    /// <summary>The picture and its frame are written on the worker; the render had to be on this frame, the disk does not.</summary>
    private void PostWrite(MapView view, MapShot shot, string folder, byte[] png, string trigger, int mask) =>
        _worker.Post(() =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, shot.FileStem + ".png"), png);
                File.WriteAllText(Path.Combine(folder, shot.FileStem + ".json"), _frames.Write(FrameDoc.Of(view, shot)) + "\n", Utf8);
                _log($"map picture {view.Location}/{shot.FileStem} ({trigger}): {shot.Frame.Width}x{shot.Frame.Height}, turned {view.Yaw:0.#} deg, floor {view.FloorId} at {view.FloorY:0.##} m, layers {mask:X}");
            }
            catch (Exception ex)
            {
                _log($"map picture {view.Location}/{shot.FileStem} failed: {ex}");
            }
        });

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
    /// pointed straight down, turned by the frame's yaw: picture right is the frame's U axis, picture up its V axis.
    /// Each tile is drawn into an antialiased target, resolved into a plain one and read into its place in the picture.
    /// </summary>
    private static (byte[] Png, bool IsBlank) Render(MapShot shot, int mask)
    {
        var frame = shot.Frame;
        var holder = new GameObject("Data Dump map camera");
        var picture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGB24, false);

        try
        {
            var camera = holder.AddComponent<Camera>();

            if (Camera.main is { } main) camera.CopyFrom(main);

            camera.enabled = false;
            camera.cullingMask = mask;
            camera.orthographic = true;
            camera.nearClipPlane = shot.Near;
            camera.farClipPlane = shot.Far;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            foreach (var tile in frame.Tiles(TileSize)) RenderTile(camera, shot, tile, picture);

            picture.Apply();

            return (ImageConversion.EncodeToPNG(picture), MapViews.IsBlank(Samples(picture, frame.Width, frame.Height)));
        }
        finally
        {
            Object.Destroy(picture);
            Object.Destroy(holder);
        }
    }

    private static void RenderTile(Camera camera, MapShot shot, MapTile tile, Texture2D picture)
    {
        var frame = shot.Frame;
        var (x, z) = frame.WorldOf(tile.CenterU, tile.CenterV);
        var smooth = new RenderTexture(tile.Width, tile.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = Antialiasing };
        var plain = new RenderTexture(tile.Width, tile.Height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;

        try
        {
            camera.targetTexture = smooth;
            camera.orthographicSize = tile.Height / (2 * frame.PixelsPerMeter);
            camera.aspect = (float)tile.Width / tile.Height;
            camera.transform.SetPositionAndRotation(new Vector3(x, shot.CameraY, z), Quaternion.Euler(90f, frame.Yaw, 0f));
            camera.Render();
            Graphics.Blit(smooth, plain);

            RenderTexture.active = plain;
            picture.ReadPixels(new Rect(0, 0, tile.Width, tile.Height), tile.PixelX, tile.PixelY);
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            smooth.Release();
            plain.Release();
            Object.Destroy(smooth);
            Object.Destroy(plain);
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

    /// <summary>A view worked out, with its key built once rather than at every look.</summary>
    private sealed record ViewOnScreen(MapView View, string Key);

    /// <summary>Everything a view is worked out from. The layout is compared as the same object: one per location.</summary>
    private readonly struct ViewKey : IEquatable<ViewKey>
    {
        public ViewKey(MapLayout layout, int floorId, WorldPoint min, WorldPoint max)
        {
            Layout = layout;
            FloorId = floorId;
            Min = min;
            Max = max;
        }

        public MapLayout Layout { get; }

        public int FloorId { get; }

        public WorldPoint Min { get; }

        public WorldPoint Max { get; }

        public bool Equals(ViewKey other) => ReferenceEquals(Layout, other.Layout) && FloorId == other.FloorId && Min.Equals(other.Min) && Max.Equals(other.Max);

        public override bool Equals(object? obj) => obj is ViewKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(FloorId, Min, Max);
    }

    /// <summary>
    /// What a location's pictures need from its map, read once per location: its camera areas and its nodes. It holds
    /// the map it was read from, so the game cannot reuse that map's address for another while it is compared.
    /// </summary>
    private sealed record MapLayout(Config_Map Map, string Name, List<MapArea> Areas, List<WorldPoint> Nodes, Dictionary<string, WorldPoint> Named, List<(WorldPoint Point, float Yaw)> Turns)
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

            var turns = new List<(WorldPoint Point, float Yaw)>();

            foreach (var rotations in new[] { map.NodeRotationMap, map.BuildingNodeRotationMap, map.PackageNodeRotationMap })
            {
                if (rotations is null) continue;

                foreach (var pair in rotations)
                {
                    if (!string.IsNullOrEmpty(pair.Key) && named.TryGetValue(pair.Key, out var point)) turns.Add((point, YawOf(pair.Value)));
                }
            }

            return new MapLayout(map, string.IsNullOrEmpty(map.Name) ? "unnamed" : map.Name, areas, nodes, named, turns);
        }

        /// <summary>The turn about the vertical, in degrees; the game's nodes are only turned that way.</summary>
        private static float YawOf(Quaternion rotation) => 2 * MathF.Atan2(rotation.y, rotation.w) * 180 / MathF.PI;
    }

    private const string ProjectionText = "U = X cos(Yaw) - Z sin(Yaw); V = X sin(Yaw) + Z cos(Yaw) (Yaw in degrees); pixel x = (U - MinU) * PixelsPerMeter from the left; pixel y = (MaxV - V) * PixelsPerMeter from the top";

    /// <summary>The .json beside a picture: what the site needs to put a world position on it.</summary>
    private sealed record FrameDoc(
        string Location,
        string Area,
        int FloorId,
        string Mode,
        int Width,
        int Height,
        float PixelsPerMeter,
        float Yaw,
        float MinU,
        float MaxU,
        float MinV,
        float MaxV,
        float FloorY,
        float CameraY,
        float Near,
        float Far,
        string Projection)
    {
        public static FrameDoc Of(MapView view, MapShot shot)
        {
            var frame = shot.Frame;

            return new FrameDoc(view.Location, view.Area.Name, view.FloorId, shot.Mode, frame.Width, frame.Height, frame.PixelsPerMeter, frame.Yaw, frame.MinU, frame.MaxU, frame.MinV, frame.MaxV,
                frame.FloorY, shot.CameraY, shot.Near, shot.Far, ProjectionText);
        }
    }
}
