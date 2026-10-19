using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class EnumBookTests
{
    private static EnumType Type(string name, params (long Value, string Name)[] values) =>
        new(name, values.Select(value => new KeyValuePair<long, string>(value.Value, value.Name)).ToList());

    // Sorted output means two dumps from two game builds diff line by line.
    [Fact]
    public void Types_sort_by_name_and_values_by_number()
    {
        var json = EnumBook.ToJson(new[] { Type("B.Weather", (10, "Rain"), (-1, "None"), (2, "Sunny")), Type("A.Slot", (0, "Head")) });

        Assert.Equal("{\n  \"A.Slot\": {\n    \"0\": \"Head\"\n  },\n  \"B.Weather\": {\n    \"-1\": \"None\",\n    \"2\": \"Sunny\",\n    \"10\": \"Rain\"\n  }\n}\n", json);
    }

    // A JSON object keeps one name per key; without joining, an alias would silently replace the other name.
    [Fact]
    public void Names_sharing_a_value_are_all_kept()
    {
        var json = EnumBook.ToJson(new[] { Type("E", (1, "First"), (1, "Default")) });

        Assert.Contains("\"1\": \"First|Default\"", json);
    }

    [Fact]
    public void No_enums_give_an_empty_object()
    {
        Assert.Equal("{}\n", EnumBook.ToJson(Array.Empty<EnumType>()));
        Assert.Equal("{\n  \"E\": {}\n}\n", EnumBook.ToJson(new[] { Type("E") }));
    }
}
