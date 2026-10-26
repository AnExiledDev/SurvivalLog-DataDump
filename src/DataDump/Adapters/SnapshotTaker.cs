using System;
using System.IO;
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
/// The game is read on the main thread, in the frame of the press; the JSON of plain documents, the files, the log
/// copies, the mod health and the pruning are the worker's, in that order.
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
    private readonly SerialWorker _worker;
    private PageCapture? _pages;

    /// <param name="root">The snapshots folder.</param>
    /// <param name="healthFolder">Where the latest mod health is kept; each snapshot refreshes it as well as saving its own.</param>
    public SnapshotTaker(string root, ModHealthProbe health, string healthFolder, Action<string> log, SerialWorker worker)
    {
        _root = root;
        _health = health;
        _healthFolder = healthFolder;
        _log = log;
        _worker = worker;
    }

    /// <summary>Whether a snapshot is still waiting for its page HTML; only then does <see cref="Tick"/> have work.</summary>
    public bool IsWaiting => _pages is not null;

    public void Take(DateTime now)
    {
        if (_pages is not null)
        {
            _log("snapshot not taken: the last one is still waiting for its page HTML (up to 10 seconds)");

            return;
        }

        // Made here, not on the worker: Unity writes the screenshot into it at the end of this frame.
        Directory.CreateDirectory(_root);

        var name = SnapshotFolders.NameFor(now, taken => Directory.Exists(Path.Combine(_root, taken)));
        var folder = Path.Combine(_root, name);

        Directory.CreateDirectory(folder);
        Part("screenshot", () => ScreenCapture.CaptureScreenshot(Path.Combine(folder, "screenshot.png")));
        Part("state.json", () => PostJson(folder, "state.json", GameState.Read(now)));
        Part("the web UI", () => SaveUi(folder, now));

        var playerLog = LogTails.PlayerLogPath(_log);
        var saved = $"snapshot saved to {folder}" + (_pages is null ? "" : "; page HTML follows within 10 seconds");

        PostPart("logs", () => LogTails.CopyTo(Path.Combine(folder, "logs"), playerLog, _log));
        _health.Post("snapshot", folder, _healthFolder);
        PostPart("old snapshots", Prune);
        _worker.Post(() => _log(saved));
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

        PostJson(folder, "ui.json", document);

        foreach (var page in pages)
        {
            if (page.State is null) continue;

            // The Redux state is the game's, so it is read here; only its file is the worker's.
            Part("redux state of " + page.Key, () => PostWrite(folder, "redux-" + FileNames.Safe(page.Key) + ".json", redux.Write(page.State)));
        }

        _pages = PageCapture.Start(folder, GameSingleton.Existing<ReduxUISystem>()?.webUILayer, pages, now, _log, _worker);
    }

    /// <summary>Deletes the oldest snapshots past the cap; only folders named like a snapshot are ever touched. On the worker.</summary>
    private void Prune()
    {
        var paths = Directory.GetDirectories(_root);
        var names = new string[paths.Length];

        for (var i = 0; i < paths.Length; i++) names[i] = Path.GetFileName(paths[i]) ?? "";

        foreach (var old in SnapshotFolders.ToDelete(names)) Directory.Delete(Path.Combine(_root, old), recursive: true);
    }

    /// <summary>A document of plain values, read from the game already: its JSON and its file are the worker's.</summary>
    private void PostJson(string folder, string fileName, object document) =>
        PostPart(fileName, () => Write(folder, fileName, new ValueWriter(new PlainValueReader(), StateDepth, isIndented: true).Write(document)));

    private void PostWrite(string folder, string fileName, string json) => PostPart(fileName, () => Write(folder, fileName, json));

    private void PostPart(string what, Action write) => _worker.Post(() => Part(what, write));

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
