using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class FileShapeTests
{
    // The dump writes one item per step across frames; joined this way the file is valid whatever the count.
    private static string Assemble(FileShape shape, params string[] items)
    {
        var text = shape.Start;

        for (var index = 0; index < items.Length; index++) text += shape.Before(index) + items[index];

        return text + shape.Finish(items.Length);
    }

    [Fact]
    public void Json_lines_put_one_value_on_each_line()
    {
        Assert.Equal("{\"ID\":1}\n{\"ID\":2}\n", Assemble(FileShape.JsonLines, "{\"ID\":1}", "{\"ID\":2}"));
    }

    [Fact]
    public void An_empty_table_is_an_empty_file()
    {
        Assert.Equal("", Assemble(FileShape.JsonLines));
    }

    [Fact]
    public void A_json_object_file_has_one_member_on_each_line()
    {
        var text = Assemble(FileShape.JsonObject, FileShape.Entry("k", "v"), FileShape.Entry("n", null));

        Assert.Equal("{\n  \"k\": \"v\",\n  \"n\": null\n}\n", text);
    }

    [Fact]
    public void An_empty_json_object_file_is_still_valid_json()
    {
        Assert.Equal("{\n}\n", Assemble(FileShape.JsonObject));
    }
}
