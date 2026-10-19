using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate;
using UnityEngine;

namespace DataDump.Adapters;

/// <summary>
/// Writes every config table, both language maps and the hotkeys to the dump folder, then manifest.json. The work is
/// spread over frames on the main thread, a few milliseconds each. Each file is written beside its final name and
/// moved there when it is complete, so a file that failed half way is never left looking whole; a file that fails is
/// deleted, logged and listed in the manifest, and the next file goes on.
/// </summary>
internal sealed class TableDumper
{
    public const string ManifestFile = "manifest.json";

    private const double FrameBudgetMilliseconds = 6;
    private const string PartSuffix = ".part";
    private const string TablePrefix = "_Config_";
    private const string TableSuffix = "_Dict";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly string _plugin;
    private readonly Action<string> _log;
    private Run? _run;

    public TableDumper(string folder, string plugin, Action<string> log)
    {
        _folder = folder;
        _plugin = plugin;
        _log = log;
    }

    public bool IsRunning => _run is not null;

    private sealed class Run
    {
        public Run(string trigger, DateTimeOffset startedAt, string? language, Queue<DumpJob> jobs, List<DumpOutcome> tables)
        {
            Trigger = trigger;
            StartedAt = startedAt;
            Language = language;
            Jobs = jobs;
            Tables = tables;
        }

        public string Trigger { get; }

        public DateTimeOffset StartedAt { get; }

        public string? Language { get; }

        public Queue<DumpJob> Jobs { get; }

        public List<DumpOutcome> Tables { get; }

        public List<DumpOutcome> Files { get; } = new();

        public Writing? Current { get; set; }
    }

    private sealed record Writing(DumpJob Job, StreamWriter Writer, IEnumerator<bool> Steps, string PartPath);

    /// <summary>Starts a dump; false, with a log line, while one is running or before the game has made its configs.</summary>
    public bool Start(string trigger)
    {
        if (_run is not null)
        {
            _log($"dump ({trigger}) not started: the last one is still writing");

            return false;
        }

        var configs = GameSingleton.Existing<ConfigManager>();

        if (configs is null)
        {
            _log($"dump ({trigger}) not started: the game has not loaded its configs yet");

            return false;
        }

        Directory.CreateDirectory(_folder);

        var rows = new RowWriter(new Il2CppValueReader());
        var tables = new List<DumpOutcome>();
        var jobs = new Queue<DumpJob>();

        foreach (var table in TableProperties())
        {
            var name = TableName(table);

            if (UnkeyedReason(table) is { } reason) tables.Add(new DumpSkipped(name, reason));
            else jobs.Enqueue(new TableJob(name, () => table.GetValue(configs), rows));
        }

        jobs.Enqueue(new LanguageJob("lang-zh", () => configs.customCache?.ChineseLanguageMap));
        jobs.Enqueue(new LanguageJob("lang-en", () => configs.customCache?.EnglishLanguageMap));
        jobs.Enqueue(new InputJob());
        jobs.Enqueue(new EnumJob(_log));

        _run = new Run(trigger, DateTimeOffset.Now, LanguageOf(configs), jobs, tables);
        _log($"dump ({trigger}) started: {jobs.Count(job => job.IsTable)} tables to {_folder}");

        return true;
    }

    /// <summary>Writes for one frame's budget. Never throws: every game read happens inside a job step.</summary>
    public void Tick()
    {
        if (_run is not { } run) return;

        var clock = Stopwatch.StartNew();

        while (clock.Elapsed.TotalMilliseconds < FrameBudgetMilliseconds)
        {
            if (run.Current is null)
            {
                if (run.Jobs.Count == 0)
                {
                    Finish(run);

                    return;
                }

                Open(run, run.Jobs.Dequeue());

                continue;
            }

            Step(run, run.Current);
        }
    }

    /// <summary>Every ConfigManager table: a _Config_X_Dict member, by name so two dumps list tables the same way.</summary>
    private static IEnumerable<PropertyInfo> TableProperties() =>
        typeof(ConfigManager).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name.StartsWith(TablePrefix, StringComparison.Ordinal) && property.Name.EndsWith(TableSuffix, StringComparison.Ordinal))
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    /// <summary>_Config_Action_Dict is the table Config_Action.</summary>
    private static string TableName(PropertyInfo table) => table.Name[1..^TableSuffix.Length];

    /// <summary>
    /// A table keyed by something other than one number, text or enum has no id to put on a row.
    /// ConfigManager._Config_DailyWeather_DayHour_Dict (keyed by a pair of ints, ConfigManager.cs:8749) is the one
    /// such table; it holds Config_DailyWeather rows, as _Config_DailyWeather_Dict does.
    /// </summary>
    private static string? UnkeyedReason(PropertyInfo table)
    {
        var arguments = table.PropertyType.IsGenericType ? table.PropertyType.GetGenericArguments() : Type.EmptyTypes;

        if (arguments.Length != 2) return "not a dictionary: " + table.PropertyType.Name;

        var key = arguments[0];

        return key.IsPrimitive || key.IsEnum || key == typeof(string) ? null : $"keyed by {key.Name}, not one id; its rows are not written";
    }

    private static string? LanguageOf(ConfigManager configs)
    {
        try
        {
            return configs.customCache?.LanguageType.ToString();
        }
        catch (Exception ex)
        {
            return ValueWriter.ErrorText(ex);
        }
    }

    private void Open(Run run, DumpJob job)
    {
        var partPath = Path.Combine(_folder, job.FileName + PartSuffix);

        try
        {
            var writer = new StreamWriter(partPath, append: false, Utf8);

            run.Current = new Writing(job, writer, job.Steps(writer).GetEnumerator(), partPath);
        }
        catch (Exception ex)
        {
            Record(run, job, new DumpFailed(job.Name, ProblemList.Reason(ex)));
            _log($"dump: could not open {job.FileName}: {ProblemList.Reason(ex)}");
        }
    }

    private void Step(Run run, Writing writing)
    {
        try
        {
            if (writing.Steps.MoveNext()) return;

            Close(writing);
            run.Current = null;
            Complete(run, writing);
        }
        catch (Exception ex)
        {
            run.Current = null;
            Abandon(writing);
            Record(run, writing.Job, new DumpFailed(writing.Job.Name, ProblemList.Reason(ex)));
            _log($"dump: {writing.Job.Name} failed after {writing.Job.Count} items and was not written: {ex}");
        }
    }

    private void Complete(Run run, Writing writing)
    {
        var job = writing.Job;
        var finalPath = Path.Combine(_folder, job.FileName);

        if (job.SkipReason is { } reason)
        {
            DeleteQuietly(writing.PartPath);
            DeleteQuietly(finalPath);
            Record(run, job, new DumpSkipped(job.Name, reason));

            return;
        }

        File.Move(writing.PartPath, finalPath, overwrite: true);
        Record(run, job, new Dumped(job.Name, job.Count));
    }

    /// <summary>A failed file is removed with its last good copy, so no file in the folder disagrees with the manifest.</summary>
    private void Abandon(Writing writing)
    {
        try
        {
            Close(writing);
        }
        catch (Exception)
        {
            // The file is deleted next; a writer that will not close changes nothing about that.
        }

        DeleteQuietly(writing.PartPath);
        DeleteQuietly(Path.Combine(_folder, writing.Job.FileName));
    }

    private static void Close(Writing writing)
    {
        writing.Steps.Dispose();
        writing.Writer.Dispose();
    }

    private static void Record(Run run, DumpJob job, DumpOutcome outcome)
    {
        if (job.IsTable) run.Tables.Add(outcome);
        else run.Files.Add(outcome);
    }

    private void Finish(Run run)
    {
        _run = null;

        var input = new ManifestInput(_plugin, GameVersion(), run.Trigger, run.StartedAt, DateTimeOffset.Now, run.Language, run.Tables, run.Files);
        Manifest.Document manifest;

        try
        {
            manifest = Manifest.Build(input);
        }
        catch (Exception ex)
        {
            run.Files.Add(new DumpFailed(ManifestFile, "could not be built: " + ProblemList.Reason(ex)));
            _log($"dump ({run.Trigger}) done without {ManifestFile}; failed: {FailureList(run)}");

            return;
        }

        try
        {
            var partPath = Path.Combine(_folder, ManifestFile + PartSuffix);

            File.WriteAllText(partPath, Manifest.ToJson(input) + "\n", Utf8);
            File.Move(partPath, Path.Combine(_folder, ManifestFile), overwrite: true);
        }
        catch (Exception ex)
        {
            _log("dump: could not write the manifest: " + ex.Message);
        }

        _log($"dump ({run.Trigger}) done in {manifest.Seconds} s: {manifest.TableCount} tables, {manifest.RowCount} rows, " +
             $"{manifest.Failed.Count} failed, {manifest.Skipped.Count} skipped (see {ManifestFile})");
    }

    /// <summary>Every failure of the run as "name: reason", for the log when there is no manifest to hold them.</summary>
    private static string FailureList(Run run) =>
        string.Join("; ", run.Tables.Concat(run.Files).OfType<DumpFailed>().Select(failed => failed.Name + ": " + failed.Reason));

    private static string? GameVersion()
    {
        try
        {
            return Application.version;
        }
        catch (Exception)
        {
            return null;
        }
    }


    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // A file that cannot be deleted is listed as failed in the manifest anyway.
        }
    }
}
