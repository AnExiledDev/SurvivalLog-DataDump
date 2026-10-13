using System;
using System.IO;
using System.Linq;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate.ReduxUI;
using UnityEngine;

namespace DataDump.Adapters;

/// <summary>
/// One bug snapshot per key press, in snapshots/yyyyMMdd-HHmmss: a screenshot, the world and its characters, the web
/// UI with each open page's Redux state, each page's HTML as the web view answers, the end of every log, and the mod
/// health. Each part is written on its own, so one that fails costs only itself. Only the 50 newest snapshots are kept.
/// Unity writes the screenshot at the end of the frame, so it shows the frame the key was pressed in, menus included.
/// Main thread only.
/// </summary>
internal sealed class SnapshotTaker
{
    private const int StateDepth = 8;
    private const int ReduxDepth = 6;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;
    private readonly ModHealthProbe _health;
    private readonly string _healthFolder;
    private readonly Action<string> _log;
    private PageCapture? _pages;

    /// <param name="root">The snapshots folder.</param>
    /// <param name="healthFolder">Where the latest mod health is kept; each snapshot refreshes it as well as saving its own.</param>
    public SnapshotTaker(string root, ModHealthProbe health, string healthFolder, Action<string> log)
    {
        _root = root;
        _health = health;
        _healthFolder = healthFolder;
        _log = log;
    }

    public void Take(DateTime now)
    {
        if (_pages is not null)
        {
            _log("snapshot not taken: the last one is still waiting for its page HTML (up to 10 seconds)");

            return;
        }

        Directory.CreateDirectory(_root);

        var name = SnapshotFolders.NameFor(now, taken => Directory.Exists(Path.Combine(_root, taken)));
        var folder = Path.Combine(_root, name);

        Directory.CreateDirectory(folder);
        Part("screenshot", () => ScreenCapture.CaptureScreenshot(Path.Combine(folder, "screenshot.png")));
        Part("state.json", () => Write(folder, "state.json", new ValueWriter(new PlainValueReader(), StateDepth, isIndented: true).Write(GameState.Read(now))));
        Part("the web UI", () => SaveUi(folder, now));
        Part("logs", () => LogTails.CopyTo(Path.Combine(folder, "logs"), _log));
        Part("mod health", () => _health.WriteTo("snapshot", folder, _healthFolder));
        Part("old snapshots", Prune);
        _log($"snapshot saved to {folder}" + (_pages is null ? "" : "; page HTML follows within 10 seconds"));
    }

    /// <summary>Writes the page HTML that has come in; the capture ends at its deadline.</summary>
    public void Tick(DateTime now)
    {
        if (_pages is not null && _pages.Tick(now)) _pages = null;
    }

    /// <summary>
    /// ui.json, then each open page's Redux state as redux-(page).json, then the page HTML capture.
    /// </summary>
    private void SaveUi(string folder, DateTime now)
    {
        var reader = new Il2CppValueReader();
        var (document, pages) = UiState.Read(now, reader);
        var redux = new ValueWriter(reader, ReduxDepth, isIndented: true);

        Write(folder, "ui.json", new ValueWriter(new PlainValueReader(), StateDepth, isIndented: true).Write(document));

        foreach (var page in pages.Where(page => page.State is not null))
        {
            Part("redux state of " + page.Key, () => Write(folder, "redux-" + FileNames.Safe(page.Key) + ".json", redux.Write(page.State)));
        }

        _pages = PageCapture.Start(folder, GameSingleton.Existing<ReduxUISystem>()?.webUILayer, pages, now, _log);
    }

    /// <summary>Deletes the oldest snapshots past the cap; only folders named like a snapshot are ever touched.</summary>
    private void Prune()
    {
        var names = Directory.GetDirectories(_root).Select(path => Path.GetFileName(path) ?? "");

        foreach (var old in SnapshotFolders.ToDelete(names)) Directory.Delete(Path.Combine(_root, old), recursive: true);
    }

    private void Part(string what, Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex)
        {
            _log($"snapshot: {what} failed: {ProblemList.Reason(ex)}");
        }
    }

    private static void Write(string folder, string fileName, string json) => File.WriteAllText(Path.Combine(folder, fileName), json + "\n", Utf8);
}
