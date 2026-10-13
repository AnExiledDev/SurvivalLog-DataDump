using System;
using System.Collections.Generic;
using System.Globalization;
using DataDump.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Show;
using GameCore.HotUpdate.ReduxUI;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine.UI;
using CameraTouch = GameCore.Scripts.CameraTouch;

namespace DataDump.Adapters;

/// <summary>One open web page: what the page capture asks the web view for, and its Redux state.</summary>
/// <param name="Key">The page's key in WebUILayer.PageMap.</param>
/// <param name="Url">WebUIVm.Url, the page file the game loads into its frame.</param>
/// <param name="State">WebUIVm.boundState.</param>
internal sealed record OpenPage(string Key, string? Url, BaseReduxState? State);

/// <summary>
/// Reads the web UI for a snapshot's ui.json: the open pages (WebUILayer.PageMap and PageList), whether the game counts
/// each as shown, and what decides whether clicks reach the world. Only reads: ReduxUISystem.IsUIActive (rva 0x2852840)
/// and WebUILayer.IsPageActive (rva 0x2b52200) are dictionary lookups, and the rest are fields.
/// Main thread only, like every Il2Cpp read.
/// </summary>
internal static class UiState
{
    public static (UiDocument Document, IReadOnlyList<OpenPage> Pages) Read(DateTime now, Il2CppValueReader reader)
    {
        var problems = new ProblemList();
        var takenAt = now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var clicks = Clicks(problems);
        var ui = GameSingleton.Existing<ReduxUISystem>();
        var layer = ui is null ? null : problems.Object("the web layer", () => ui.webUILayer);

        if (ui is null || layer is null)
        {
            problems.Add(ui is null ? "the game has not made its UI system" : "the UI system has no web layer");

            return (new UiDocument(takenAt, ui is not null, null, null, null, Array.Empty<PageDoc>(), clicks, problems.Lines), Array.Empty<OpenPage>());
        }

        var pages = new List<OpenPage>();
        var docs = problems.Object("the open pages", () => Pages(ui, layer, reader, pages, problems)) ?? new List<PageDoc>();
        var document = new UiDocument(
            takenAt,
            true,
            problems.Value("web layer initialized", () => layer.IsInitialized),
            problems.Value("browser crashed", () => layer.isBrowserCrashed),
            problems.Object("web view url", () => layer.canvasWebViewPrefab?.WebView?.Url),
            docs,
            clicks,
            problems.Lines);

        return (document, pages);
    }

    private static List<PageDoc> Pages(ReduxUISystem ui, WebUILayer layer, Il2CppValueReader reader, List<OpenPage> pages, ProblemList problems)
    {
        var docs = new List<PageDoc>();
        var map = layer.PageMap;

        if (map is null) return docs;

        var listed = ListedPages(layer);
        var ready = layer.pageReadyStates;

        foreach (var entry in map)
        {
            var key = entry.Key;
            var vm = entry.Value;

            if (key is null || vm is null) continue;

            var uiType = problems.Object(key + " type", () => vm.UIType);
            var url = problems.Object(key + " url", () => vm.Url);
            var state = problems.Object(key + " state", () => vm.boundState);

            docs.Add(new PageDoc(
                key,
                problems.Object(key + " ui key", () => vm.UIKey),
                url,
                uiType?.FullName,
                uiType is null ? null : problems.Value(key + " shown", () => ui.IsUIActive(uiType)),
                problems.Value(key + " active", () => layer.IsPageActive(key)),
                ready is not null && ready.TryGetValue(key, out var isReady) ? isReady : null,
                problems.Value(key + " z index", () => vm.ZIndex),
                problems.Value(key + " instance id", () => vm.InstanceId),
                listed.Contains(vm.Pointer),
                state is null ? null : problems.Object(key + " state type", () => reader.Narrow(state).GetType().Name)));
            pages.Add(new OpenPage(key, url, state));
        }

        return docs;
    }

    /// <summary>The pages in WebUILayer.PageList, by pointer: the list the layer walks, which PageMap should match.</summary>
    private static HashSet<IntPtr> ListedPages(WebUILayer layer)
    {
        var listed = new HashSet<IntPtr>();
        var list = layer.PageList;

        if (list is null) return listed;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is Il2CppObjectBase page) listed.Add(page.Pointer);
        }

        return listed;
    }

    /// <summary>
    /// CameraTouch's Image is what a click on the world lands on; menus switch its raycastTarget off while they are
    /// open. CameraManager.isBattleSelectOpen is set while the object menu is up.
    /// </summary>
    private static ClickDoc Clicks(ProblemList problems)
    {
        var camera = problems.Object("the camera", () => GameSingleton.Existing<BattleShowWorld>()?._CameraManager);
        var isRaycastTarget = problems.MaybeValue("world clicks", () =>
        {
            var touch = CameraTouch.Instance;
            var image = touch == null ? null : touch.GetComponent<Image>();

            return image == null ? null : image.raycastTarget;
        });

        return new ClickDoc(
            isRaycastTarget,
            camera is null ? null : problems.Value("object menu flag", () => camera.isBattleSelectOpen),
            camera is null ? null : problems.Value("camera pause", () => camera.isTimePaused));
    }
}
