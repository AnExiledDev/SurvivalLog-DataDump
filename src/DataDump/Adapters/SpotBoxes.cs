using System;
using System.Collections.Generic;
using DataDump.Core;
using GameCore.HotUpdate;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DataDump.Adapters;

/// <summary>
/// Reads the boxes the game's layout view draws for a home's furniture and building spots. MapNodeSearch.ShowBuildingPos
/// turns on the scene node "BuildingPos" under the map's root; each child of it is one spot, named like its MapSlot row,
/// and holds a "BuildingPos_Cube" whose mesh is the box the player sees outlined. The node is there, switched off,
/// whenever the home is loaded, so it can be read without opening the layout view.
/// </summary>
internal static class SpotBoxes
{
    private const string RootName = "BuildingPos";
    private const string CubeName = "BuildingPos_Cube";

    /// <summary>SlotType 4. The game stands a wall spot's cube up only when it shows it (ApplyWallSlotCubePose).</summary>
    private const int WallType = 4;

    /// <summary>MapNodeSearch's static constructor: WallSlotCubeLocalRotation from Euler (-pi/2, 0, 0) radians.</summary>
    private static readonly Quaternion WallRotation = Quaternion.Euler(-90f, 0f, 0f);

    /// <summary>MapNodeSearch's static constructor: WallSlotCubeLocalScale.</summary>
    private static readonly Vector3 WallScale = new(1f, 0.75f, 1f);

    /// <summary>The spots of the loaded home, or null when no scene has a BuildingPos node (an expedition, a store).</summary>
    public static List<SpotOutline>? Read()
    {
        if (FindRoot() is not { } root) return null;

        var config = GameSingleton.Existing<ConfigManager>();
        var spots = new List<SpotOutline>();

        for (var i = 0; i < root.childCount; i++)
        {
            var spot = root.GetChild(i);
            var cube = spot.Find(CubeName);

            if (cube == null) continue;

            var name = spot.name;
            var type = config?.GetMapSlotTypeByName(name) ?? 0;
            var toWorld = type == WallType ? spot.localToWorldMatrix * Matrix4x4.TRS(Vector3.zero, WallRotation, WallScale) : cube.localToWorldMatrix;

            spots.Add(SpotOutlines.Of(name, type, PointOf(spot.position), Corners(toWorld, MeshBounds(cube))));
        }

        return spots;
    }

    /// <summary>
    /// The map root is a scene's root object or sits one level down; BuildingPos is its child. Transform.Find also finds
    /// switched-off children.
    /// </summary>
    private static Transform? FindRoot()
    {
        for (var s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);

            if (!scene.isLoaded) continue;

            foreach (var top in scene.GetRootGameObjects())
            {
                var transform = top.transform;

                if (transform.Find(RootName) is { } direct) return direct;

                for (var c = 0; c < transform.childCount; c++)
                {
                    if (transform.GetChild(c).Find(RootName) is { } nested) return nested;
                }
            }
        }

        return null;
    }

    /// <summary>The cube's mesh box; Unity's own cube (one meter each way about its middle) when it has none.</summary>
    private static Bounds MeshBounds(Transform cube)
    {
        var filter = cube.GetComponent<MeshFilter>();
        var mesh = filter == null ? null : filter.sharedMesh;

        return mesh == null ? new Bounds(Vector3.zero, Vector3.one) : mesh.bounds;
    }

    private static WorldPoint[] Corners(Matrix4x4 toWorld, Bounds box)
    {
        var corners = new WorldPoint[8];
        var (center, extents) = (box.center, box.extents);

        for (var i = 0; i < 8; i++)
        {
            var local = new Vector3(
                center.x + ((i & 1) == 0 ? -extents.x : extents.x),
                center.y + ((i & 2) == 0 ? -extents.y : extents.y),
                center.z + ((i & 4) == 0 ? -extents.z : extents.z));

            corners[i] = PointOf(toWorld.MultiplyPoint3x4(local));
        }

        return corners;
    }

    private static WorldPoint PointOf(Vector3 position) => new(position.x, position.y, position.z);
}
