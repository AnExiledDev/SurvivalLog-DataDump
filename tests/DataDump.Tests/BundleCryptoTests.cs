using System.Text;
using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class BundleCryptoTests
{
    // Worked out apart from the mod (Python hashlib): MD5(salt + name) then MD5(name + salt).
    [Fact]
    public void The_key_is_the_two_salted_md5s_of_the_name()
    {
        var key = BundleCrypto.Key("assets_texture_item.bundle");

        Assert.Equal("609f3740a72bc368d7538cafae37e4eef4e864478f155b2f5a19ca7ce620e8db", Convert.ToHexString(key).ToLowerInvariant());
    }

    // The export reads in chunks; a chunk that starts mid-file must use the key from that offset, or the bundle is garbage.
    [Fact]
    public void Decrypting_in_chunks_gives_the_same_bytes_as_all_at_once()
    {
        var key = BundleCrypto.Key("a.bundle");
        var plain = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var whole = plain.ToArray();
        var chunked = plain.ToArray();

        BundleCrypto.Apply(whole, 0, key);
        BundleCrypto.Apply(chunked.AsSpan(0, 45), 0, key);
        BundleCrypto.Apply(chunked.AsSpan(45), 45, key);

        Assert.Equal(whole, chunked);
        Assert.NotEqual(plain, whole);

        BundleCrypto.Apply(whole, 0, key);

        Assert.Equal(plain, whole);
    }

    [Fact]
    public void Only_the_right_name_opens_an_encrypted_bundle()
    {
        var right = BundleCrypto.Key("battlescene_school.bundle");
        var wrong = BundleCrypto.Key("battlescene_office.bundle");
        var head = Encoding.ASCII.GetBytes("UnityFS\0");

        BundleCrypto.Apply(head, 0, right);

        Assert.True(BundleCrypto.Opens(head, right));
        Assert.False(BundleCrypto.Opens(head, wrong));
        Assert.False(BundleCrypto.Opens(head.AsSpan(0, 4), right));
    }

    [Fact]
    public void Manifest_names_are_found_once_each_in_order()
    {
        var manifest = Encoding.Latin1.GetBytes("\u0001\u0002ui_main.bundle\u0000xx texture_item.bundle\u0003ui_main.bundle\u00ffEND");

        Assert.Equal(new[] { "ui_main.bundle", "texture_item.bundle" }, BundleCrypto.NamesIn(manifest));
    }

    // A manifest name becomes a file name in the dump folder; none may climb out of it.
    [Theory]
    [InlineData("ui_main.bundle", true)]
    [InlineData("../x.bundle", false)]
    [InlineData("a/b.bundle", false)]
    [InlineData(".hidden.bundle", false)]
    [InlineData("a..b.bundle", false)]
    [InlineData("", false)]
    public void Only_plain_names_are_safe_as_file_names(string name, bool isSafe)
    {
        Assert.Equal(isSafe, BundleCrypto.IsSafeFileName(name));
    }

    [Theory]
    [InlineData("texture_item.bundle", "", true)]
    [InlineData("texture_item.bundle", " ,  ", true)]
    [InlineData("texture_item.bundle", "battlescene, TEXTURE", true)]
    [InlineData("ui_main.bundle", "battlescene, texture", false)]
    public void The_filter_keeps_names_containing_any_of_its_words(string name, string filter, bool isWanted)
    {
        Assert.Equal(isWanted, BundleCrypto.IsWanted(name, filter));
    }
}
