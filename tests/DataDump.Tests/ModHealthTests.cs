using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class ModHealthTests
{
    private static readonly HealthInput Input = new(
        new DateTimeOffset(2026, 10, 13, 1, 30, 0, TimeSpan.FromHours(8)),
        "load+30s",
        new[] { new PluginEntry("com.z", "Zed", "1.0.0", "Zed.dll", true), new PluginEntry("com.a", "Alpha", "0.2.0", "Alpha.dll", false) },
        new[] { "Broken: missing dependency com.q" },
        new[]
        {
            new PatchedMethod("B.Run()", new[] { new PatchEntry("com.a", PatchKind.Prefix, "APatch.Before", 400) }),
            new PatchedMethod("A.Go(Int32)", new[]
            {
                new PatchEntry("com.z", PatchKind.Postfix, "ZPatch.After", 400),
                new PatchEntry("com.a", PatchKind.Prefix, "APatch.First", 800),
            }),
        },
        Array.Empty<string>());

    // Two mods on one game method is where clashes come from; those methods are listed on their own.
    [Fact]
    public void Methods_patched_by_more_than_one_owner_are_called_out()
    {
        var document = ModHealthReport.Build(Input);
        var shared = Assert.Single(document.Shared);

        Assert.Equal("A.Go(Int32)", shared.Target);
        Assert.Equal(new[] { "com.a", "com.z" }, shared.Owners);
    }

    [Fact]
    public void A_method_patched_twice_by_one_owner_is_not_shared()
    {
        var input = Input with
        {
            Methods = new[]
            {
                new PatchedMethod("C.Tick()", new[]
                {
                    new PatchEntry("com.a", PatchKind.Prefix, "APatch.One", 400),
                    new PatchEntry("com.a", PatchKind.Postfix, "APatch.Two", 400),
                }),
            },
        };

        Assert.Empty(ModHealthReport.Build(input).Shared);
    }

    [Fact]
    public void The_text_report_lists_plugins_failures_shared_and_patched_methods()
    {
        var expected = string.Join("\n",
            "Mod health at 2026-10-13T01:30:00+08:00 (load+30s)",
            "",
            "Plugins (2):",
            "  com.a  Alpha 0.2.0  Alpha.dll  (no instance)",
            "  com.z  Zed 1.0.0  Zed.dll",
            "",
            "Failed to load (1):",
            "  Broken: missing dependency com.q",
            "",
            "Patched by more than one owner (1):",
            "  A.Go(Int32): com.a, com.z",
            "",
            "Patched methods (2):",
            "  A.Go(Int32)",
            "    com.a: prefix APatch.First [priority 800];",
            "    com.z: postfix ZPatch.After;",
            "  B.Run()",
            "    com.a: prefix APatch.Before;",
            "",
            "Could not read (0):",
            "",
            "");

        Assert.Equal(expected, ModHealthReport.ToText(ModHealthReport.Build(Input)));
    }

    [Fact]
    public void The_json_report_groups_each_method_by_owner()
    {
        var json = ModHealthReport.ToJson(ModHealthReport.Build(Input));

        Assert.Contains("\"prefixes\": [\n            \"APatch.First [priority 800]\"\n          ]", json);
        Assert.Contains("\"guid\": \"com.a\"", json);
    }
}
