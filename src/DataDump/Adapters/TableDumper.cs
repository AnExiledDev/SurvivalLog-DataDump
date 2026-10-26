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
/// Writes every config table, both language maps and the hotkeys to the dump folder, then manifest.json. The game is
/// read on the main thread, spread over frames, about 2 milliseconds each, into text in memory; each finished file is
/// handed to the worker, which writes it beside its final name and moves it there, so a file that failed half way is
/// never left looking whole. A file that fails is deleted, logged and listed in the manifest, and the next file goes on.
/// The worker also keeps the run's outcomes and writes the manifest after the last file, so the outcomes are only ever
/// touched by one thread.
/// </summary>
internal sealed class TableDumper
{
    public const string ManifestFile = "manifest.json";

    private const double FrameBudgetMilliseconds = 2;
    private const string PartSuffix = ".part";
    private const string TablePrefix = "_Config_";
    private const string TableSuffix = "_Dict";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly long FrameBudgetTicks = (long)(Stopwatch.Frequency * FrameBudgetMilliseconds / 1000);

    /// <summary>ConfigManager's tables by reflection: a few hundred properties, looked up once, on the worker at load.</summary>
    private static readonly Lazy<IReadOnlyList<Table>> Tables = new(FindTables);

    private readonly string _folder;
    private readonly string _plugin;
    private readonly Action<string> _log;
    private readonly SerialWorker _worker;
    private Run? _run;

    public TableDumper(string folder, string plugin, Action<string> log, SerialWorker worker)
    {
        _folder = folder;
        _plugin = plugin;
        _log = log;
        _worker = worker;
    }

    public bool IsRunning => _run is not null;

    /// <param name="UnkeyedReason">Why its rows are not written, for a table keyed by something other than one id.</param>
    private sealed record Table(PropertyInfo Property, string Name, string? UnkeyedReason);

    private sealed class Run
    {
        public Run(string trigger, DateTimeOffset startedAt, string? language, string? gameVersion, Queue<DumpJob> jobs, List<DumpOutcome> tables)
        {
            Trigger = trigger;
            StartedAt = startedAt;
            Language = language;
            GameVersion = gameVersion;
            Jobs = jobs;
            Tables = tables;
        }

        public string Trigger { get; }

        public DateTimeOffset StartedAt { get; }

        public string? Language { get; }

        public string? GameVersion { get; }

        public Queue<DumpJob> Jobs { get; }

        /// <summary>Touched by the worker only, once the run has started.</summary>
        public List<DumpOutcome> Tables { get; }

        /// <summary>Touched by the worker only.</summary>
        public List<DumpOutcome> Files { get; } = new();

        public Reading? Current { get; set; }
    }

    /// <summary>A file being read from the game, its text so far, and the steps left.</summary>
    private sealed record Reading(DumpJob Job, StringWriter Text, IEnumerator<bool> Steps);

    /// <summary>Looks up the tables on the worker, so the first dump does not pay for the reflection on the game's frame.</summary>
    public static void WarmUp(SerialWorker worker) => worker.Post(() => _ = Tables.Value);

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

        PostCreateFolder();

        var rows = new RowWriter(new Il2CppValueReader());
        var tables = new List<DumpOutcome>();
        var jobs = new Queue<DumpJob>();
        var tableCount = 0;

        foreach (var table in Tables.Value)
        {
            if (table.UnkeyedReason is { } reason)
            {
                tables.Add(new DumpSkipped(table.Name, reason));

                continue;
            }

            jobs.Enqueue(TableJobOf(table.Name, table.Property, configs, rows));
            tableCount++;
        }

        jobs.Enqueue(new LanguageJob("lang-zh", () => configs.customCache?.ChineseLanguageMap));
        jobs.Enqueue(new LanguageJob("lang-en", () => configs.customCache?.EnglishLanguageMap));
        jobs.Enqueue(new InputJob());
        jobs.Enqueue(new EnumJob(_log));
        jobs.Enqueue(new CodeTablesJob());

        _run = new Run(trigger, DateTimeOffset.Now, LanguageOf(configs), GameVersion(), jobs, tables);
        _log($"dump ({trigger}) started: {tableCount} tables to {_folder}");

        return true;
    }

    /// <summary>Reads for one frame's budget. Never throws: every game read happens inside a job step.</summary>
    public void Tick()
    {
        if (_run is not { } run) return;

        var stopAt = Stopwatch.GetTimestamp() + FrameBudgetTicks;

        while (Stopwatch.GetTimestamp() < stopAt)
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

    /// <summary>Its own method, so each table's reader closes over its own property.</summary>
    private static TableJob TableJobOf(string name, PropertyInfo property, ConfigManager configs, RowWriter rows) =>
        new(name, () => property.GetValue(configs), rows);

    /// <summary>Every ConfigManager table: a _Config_X_Dict member, by name so two dumps list tables the same way.</summary>
    private static IReadOnlyList<Table> FindTables() =>
        typeof(ConfigManager).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name.StartsWith(TablePrefix, StringComparison.Ordinal) && property.Name.EndsWith(TableSuffix, StringComparison.Ordinal))
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new Table(property, TableName(property), UnkeyedReason(property)))
            .ToList();

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

    private static void Open(Run run, DumpJob job)
    {
        var text = new StringWriter();

        run.Current = new Reading(job, text, job.Steps(text).GetEnumerator());
    }

    private void Step(Run run, Reading reading)
    {
        try
        {
            if (reading.Steps.MoveNext()) return;

            reading.Steps.Dispose();
            run.Current = null;
            PostComplete(run, reading);
        }
        catch (Exception ex)
        {
            run.Current = null;
            DisposeQuietly(reading.Steps);
            PostAbandon(run, reading.Job, ProblemList.Reason(ex));
            _log($"dump: {reading.Job.Name} failed after {reading.Job.Count} items and was not written: {ex}");
        }
    }

    private void PostCreateFolder() => _worker.Post(() => Directory.CreateDirectory(_folder));

    private void PostComplete(Run run, Reading reading) => _worker.Post(() => Complete(run, reading));

    private void PostAbandon(Run run, DumpJob job, string reason) => _worker.Post(() => Abandon(run, job, reason));

    /// <summary>On the worker: the job's last text, the file, the move to its final name, the outcome.</summary>
    private void Complete(Run run, Reading reading)
    {
        var job = reading.Job;
        var partPath = Path.Combine(_folder, job.FileName + PartSuffix);
        var finalPath = Path.Combine(_folder, job.FileName);

        if (job.SkipReason is { } reason)
        {
            DeleteQuietly(partPath);
            DeleteQuietly(finalPath);
            Record(run, job, new DumpSkipped(job.Name, reason));

            return;
        }

        StreamWriter file;

        try
        {
            file = new StreamWriter(partPath, append: false, Utf8);
        }
        catch (Exception ex)
        {
            Record(run, job, new DumpFailed(job.Name, ProblemList.Reason(ex)));
            _log($"dump: could not open {job.FileName}: {ProblemList.Reason(ex)}");

            return;
        }

        try
        {
            using (file)
            {
                file.Write(reading.Text.GetStringBuilder());
                job.Finish(file);
            }

            File.Move(partPath, finalPath, overwrite: true);
            Record(run, job, new Dumped(job.Name, job.Count));
        }
        catch (Exception ex)
        {
            Abandon(run, job, ProblemList.Reason(ex));
            _log($"dump: {job.Name} failed after {job.Count} items and was not written: {ex}");
        }
    }

    /// <summary>On the worker. A failed file is removed with its last good copy, so no file in the folder disagrees with the manifest.</summary>
    private void Abandon(Run run, DumpJob job, string reason)
    {
        DeleteQuietly(Path.Combine(_folder, job.FileName + PartSuffix));
        DeleteQuietly(Path.Combine(_folder, job.FileName));
        Record(run, job, new DumpFailed(job.Name, reason));
    }

    private static void Record(Run run, DumpJob job, DumpOutcome outcome)
    {
        if (job.IsTable) run.Tables.Add(outcome);
        else run.Files.Add(outcome);
    }

    /// <summary>The run is over on the main thread; the worker writes the manifest once every file before it is done.</summary>
    private void Finish(Run run)
    {
        _run = null;
        PostManifest(run);
    }

    private void PostManifest(Run run) => _worker.Post(() => WriteManifest(run));

    /// <summary>On the worker, after every file of the run.</summary>
    private void WriteManifest(Run run)
    {
        var input = new ManifestInput(_plugin, run.GameVersion, run.Trigger, run.StartedAt, DateTimeOffset.Now, run.Language, run.Tables, run.Files);
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

    /// <summary>On the main thread: Unity answers only there.</summary>
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

    private static void DisposeQuietly(IDisposable steps)
    {
        try
        {
            steps.Dispose();
        }
        catch (Exception)
        {
            // The file is not written either way.
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
