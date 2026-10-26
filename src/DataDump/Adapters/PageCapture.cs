using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate.ReduxUI;

namespace DataDump.Adapters;

/// <summary>
/// Asks the game's web view for the HTML of the root page and of every open page, and writes each answer as it comes.
/// Nothing waits: each answer arrives in a script callback and is written on a later frame; after the deadline the
/// pages that never answered are listed as timed out in page-capture.json and the capture is over.
/// Each page is an iframe in Root.html whose id is the page's PageMap key: WebUILayer.LoadPage (rva 0x2b531c0) hands
/// that key to BuildLoadPageJson (rva 0x2b4e160) as the page id, and Root.html sets frame.id = pageId (Root.html:493-494).
/// The pages are read through the frame's document, as other mods on this game already do, so they are same-origin.
/// </summary>
internal sealed class PageCapture
{
    public const string ResultFile = "page-capture.json";

    /// <summary>Root.html itself, in the capture; its file is root.html, which no page file (page-...) can be.</summary>
    private const string RootPage = "(root)";

    private const string HtmlMarker = "html:";
    private const int MaxDetailLength = 300;

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly CaptureBook _book;
    private readonly ConcurrentQueue<KeyValuePair<string, string?>> _replies = new();
    private readonly ConcurrentQueue<KeyValuePair<string, CaptureResult>> _saved = new();
    private readonly Action<string> _log;
    private readonly SerialWorker _worker;
    private int _savesUnderWay;

    private PageCapture(string folder, IEnumerable<string> pages, DateTime now, Action<string> log, SerialWorker worker)
    {
        _folder = folder;
        _book = new CaptureBook(pages, now + Wait);
        _log = log;
        _worker = worker;
    }

    /// <returns>The capture under way; null, with a log line, when the game has no web view to ask.</returns>
    public static PageCapture? Start(string folder, WebUILayer? layer, IReadOnlyList<OpenPage> pages, DateTime now, Action<string> log, SerialWorker worker)
    {
        var webView = layer?.canvasWebViewPrefab?.WebView;

        if (webView is null)
        {
            log("snapshot: the game has no web view, so no page HTML was saved");

            return null;
        }

        var names = new[] { RootPage }.Concat(pages.Select(page => page.Key)).Distinct(StringComparer.Ordinal).ToList();
        var capture = new PageCapture(folder, names, now, log, worker);

        foreach (var name in names)
        {
            try
            {
                webView.ExecuteJavaScript(Script(name), (Action<string>)(reply => capture._replies.Enqueue(new KeyValuePair<string, string?>(name, reply))));
            }
            catch (Exception ex)
            {
                capture._book.Answer(name, new CaptureResult(CaptureState.Failed, "the web view refused the script: " + ProblemList.Reason(ex)));
            }
        }

        return capture;
    }

    /// <summary>
    /// Hands each reply's HTML to the worker and books each file it saved. The capture waits past its deadline only for
    /// a reply that came in before it and is still being written, as it did when the write was made here.
    /// </summary>
    /// <returns>True once the capture is over; page-capture.json follows on the worker.</returns>
    public bool Tick(DateTime now)
    {
        while (_replies.TryDequeue(out var reply)) Accept(reply.Key, reply.Value);

        while (_saved.TryDequeue(out var saved))
        {
            _book.Answer(saved.Key, saved.Value);
            _savesUnderWay--;
        }

        if (_savesUnderWay > 0 || !_book.IsDue(now)) return false;

        PostResults(_book.Close());

        return true;
    }

    private void Accept(string page, string? reply)
    {
        if (reply is null || !reply.StartsWith(HtmlMarker, StringComparison.Ordinal))
        {
            _book.Answer(page, new CaptureResult(CaptureState.Failed, Shorten(reply ?? "no reply")));

            return;
        }

        _savesUnderWay++;
        _worker.Post(() => _saved.Enqueue(new KeyValuePair<string, CaptureResult>(page, Save(page, reply))));
    }

    private void PostResults(SortedDictionary<string, CaptureResult> results) => _worker.Post(() => WriteResults(results));

    private static string FileNameOf(string page) => page == RootPage ? "root.html" : "page-" + FileNames.Safe(page) + ".html";

    /// <summary>The page's document as text, or an error the capture lists; never throws into the page.</summary>
    private static string Script(string page)
    {
        var findDocument = page == RootPage
            ? "var d=document;"
            : "var f=document.getElementById(" + JsonText.Quote(page) + ");" +
              "if(!f||f.tagName!=='IFRAME')return 'error: no frame with this page id';" +
              "var d=f.contentDocument;";

        return "(function(){try{" + findDocument +
               "if(!d||!d.documentElement)return 'error: the frame has no document';" +
               "return '" + HtmlMarker + "'+d.documentElement.outerHTML;" +
               "}catch(e){return 'error: '+e;}})()";
    }

    /// <summary>On the worker: a reply that holds the page's HTML.</summary>
    private CaptureResult Save(string page, string reply)
    {
        var fileName = FileNameOf(page);

        try
        {
            File.WriteAllText(Path.Combine(_folder, fileName), reply[HtmlMarker.Length..], Utf8);

            return new CaptureResult(CaptureState.Saved, fileName);
        }
        catch (Exception ex)
        {
            return new CaptureResult(CaptureState.Failed, "could not write " + fileName + ": " + ProblemList.Reason(ex));
        }
    }

    /// <summary>On the worker.</summary>
    private void WriteResults(SortedDictionary<string, CaptureResult> results)
    {
        try
        {
            File.WriteAllText(Path.Combine(_folder, ResultFile), new ValueWriter(new PlainValueReader(), 3, isIndented: true).Write(results) + "\n", Utf8);
        }
        catch (Exception ex)
        {
            _log("snapshot: could not write " + ResultFile + ": " + ProblemList.Reason(ex));
        }

        _log($"snapshot pages: {results.Values.Count(result => result.State == CaptureState.Saved)} of {results.Count} saved ({_folder})");
    }

    private static string Shorten(string text) => text.Length <= MaxDetailLength ? text : text[..MaxDetailLength] + "...";
}
