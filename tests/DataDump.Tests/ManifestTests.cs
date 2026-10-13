using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class ManifestTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 13, 1, 26, 0, TimeSpan.FromHours(8));

    private static ManifestInput Input(IReadOnlyList<DumpOutcome> tables, IReadOnlyList<DumpOutcome> files) =>
        new("Data Dump 0.1.0", "1.0.5", "load", Started, Started.AddMilliseconds(2500), "zh", tables, files);

    [Fact]
    public void Rows_are_counted_over_dumped_tables_only()
    {
        var document = Manifest.Build(Input(
            new DumpOutcome[] { new Dumped("Config_B", 3), new DumpFailed("Config_Z", "boom"), new Dumped("Config_A", 2) },
            new DumpOutcome[] { new Dumped("lang-zh", 10) }));

        Assert.Equal(2, document.TableCount);
        Assert.Equal(5, document.RowCount);
        Assert.Equal(new[] { "Config_A", "Config_B" }, document.Tables.Keys);
        Assert.Equal(10, document.Files["lang-zh"]);
    }

    // A table that failed has to be visible in the manifest with its reason, or a short dump looks complete.
    [Fact]
    public void Failures_from_tables_and_files_are_listed_with_reasons_by_name()
    {
        var document = Manifest.Build(Input(
            new DumpOutcome[] { new DumpFailed("Config_Z", "boom"), new DumpSkipped("Config_C", "not a dictionary") },
            new DumpOutcome[] { new DumpFailed("input", "no defaults") }));

        Assert.Equal(new[] { new Manifest.Problem("Config_Z", "boom"), new Manifest.Problem("input", "no defaults") }, document.Failed);
        Assert.Equal(new[] { new Manifest.Problem("Config_C", "not a dictionary") }, document.Skipped);
        Assert.Empty(document.Tables);
    }

    [Fact]
    public void The_manifest_file_is_indented_json_in_a_fixed_order()
    {
        var json = Manifest.ToJson(Input(
            new DumpOutcome[] { new Dumped("Config_A", 2), new DumpFailed("Config_B", "NullReferenceException: x") },
            Array.Empty<DumpOutcome>()));
        var expected = string.Join("\n",
            "{",
            "  \"plugin\": \"Data Dump 0.1.0\",",
            "  \"gameVersion\": \"1.0.5\",",
            "  \"trigger\": \"load\",",
            "  \"startedAt\": \"2026-10-13T01:26:00+08:00\",",
            "  \"finishedAt\": \"2026-10-13T01:26:02+08:00\",",
            "  \"seconds\": 2.5,",
            "  \"language\": \"zh\",",
            "  \"tableCount\": 1,",
            "  \"rowCount\": 2,",
            "  \"tables\": {",
            "    \"Config_A\": 2",
            "  },",
            "  \"files\": {},",
            "  \"failed\": [",
            "    {",
            "      \"name\": \"Config_B\",",
            "      \"reason\": \"NullReferenceException: x\"",
            "    }",
            "  ],",
            "  \"skipped\": []",
            "}");

        Assert.Equal(expected, json);
    }
}
