using System.Linq;
using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class MapViewsTests
{
    private static WorldPoint At(float x, float z) => new(x, 0, z);

    private static readonly MapArea Home = new("Home", At(-45, -6), At(1, 23));
    private static readonly MapArea Market = new("Market", At(200, 5), At(245, 43));

    [Fact]
    public void The_game_camera_box_is_matched_to_its_camera_area()
    {
        var area = MapViews.AreaOf(new[] { Home, Market }, At(200.4f, 5.2f), At(244.7f, 43));

        Assert.Equal("Market", area?.Name);
    }

    // A box that is no area of the map must not borrow a near one's name and overwrite its pictures.
    [Fact]
    public void A_box_that_is_no_camera_area_matches_none()
    {
        Assert.Null(MapViews.AreaOf(new[] { Home, Market }, At(200, 5), At(260, 43)));
    }

    // A shelf standing just past a store's camera area still belongs in its picture; the next store does not.
    [Fact]
    public void An_area_grows_to_hold_the_nodes_just_outside_it()
    {
        var nodes = new[] { At(247, 20), At(400, 20) };
        var cover = MapViews.Cover(Market, 2, nodes);

        Assert.Equal(247 + MapViews.ViewMargin, cover.Max.X);
        Assert.Equal(200 - MapViews.ViewMargin, cover.Min.X);
    }

    // An expedition's one camera area can be a small part of the building; its picture must hold every spot.
    [Fact]
    public void The_one_area_of_a_map_grows_to_hold_every_node()
    {
        var nodes = new[] { At(30, -100), At(70, -60) };
        var cover = MapViews.Cover(new MapArea("School", At(53, -86), At(77, -68)), 1, nodes);

        var m = MapViews.ViewMargin;

        Assert.Equal((30 - m, -100 - m, 77 + m, -60 + m), (cover.Min.X, cover.Min.Z, cover.Max.X, cover.Max.Z));
    }

    // The Tech Company's walls stand past its last spot; a picture cut at the spots loses the building's edge.
    [Fact]
    public void A_picture_reaches_past_its_area_on_every_side()
    {
        var cover = MapViews.Cover(Home, 2, new WorldPoint[0]);

        Assert.True(MapViews.ViewMargin >= 10);
        Assert.Equal((-45 - MapViews.ViewMargin, 23 + MapViews.ViewMargin), (cover.Min.X, cover.Max.Z));
    }

    // The Hospital's map point names a camera area its map lacks, so the game camera's box is +-9999 m. A picture
    // of that box is 2 m a pixel and samples black; it must be taken of the place's nodes instead.
    [Fact]
    public void The_game_fallback_box_is_not_a_real_area()
    {
        Assert.True(MapViews.IsGameDefault(At(-9999, -9999), At(9999, 9999)));
        Assert.False(MapViews.IsGameDefault(At(-62, -28), At(56.6f, 33.5f)));
    }

    [Fact]
    public void A_fallback_view_is_the_box_around_the_nodes_named_for_the_place()
    {
        var area = MapViews.AroundNodes("TanSuo_Hospital", new[] { At(-57.5f, 28.6f), At(57, -27), At(0, 0) });

        Assert.Equal("TanSuo_Hospital", area?.Name);
        Assert.Equal((-57.5f, -27f, 57f, 28.6f), (area!.Min.X, area.Min.Z, area.Max.X, area.Max.Z));
        Assert.Null(MapViews.AroundNodes("empty", new WorldPoint[0]));
    }

    [Fact]
    public void A_picture_of_only_black_is_blank()
    {
        Assert.True(MapViews.IsBlank(new (byte, byte, byte)[] { (0, 0, 0), (1, 2, 0) }));
        Assert.False(MapViews.IsBlank(new (byte, byte, byte)[] { (0, 0, 0), (0, 0, 40) }));
    }
}

public class MapYawTests
{
    private static readonly MapArea Store = new("Tool_Store", new WorldPoint(0, 0, 0), new WorldPoint(20, 3, 20));

    private static (WorldPoint, float)[] Nodes(params float[] yaws) => yaws.Select((yaw, i) => (new WorldPoint(1 + i, 0, 1 + i), yaw)).ToArray();

    // The Tool Store's shelves stand at 15 degrees, some turned a further quarter turn; the picture must turn with them.
    [Fact]
    public void A_place_built_at_an_angle_turns_to_its_furniture()
    {
        Assert.Equal(15, MapViews.YawOf(Store, Nodes(15, 105, -75, 15.4f, 0)), 0);
    }

    // Most of the game is built square, and a few crates set askew must not turn the whole picture.
    [Fact]
    public void A_square_place_with_a_few_askew_nodes_stays_square()
    {
        Assert.Equal(0, MapViews.YawOf(Store, Nodes(0, 90, 180, 0, 33, 12)));
    }

    [Fact]
    public void Fewer_than_three_nodes_at_an_angle_do_not_turn_the_picture()
    {
        Assert.Equal(0, MapViews.YawOf(Store, Nodes(15, 15)));
    }

    // Nodes of the next building over stand outside the cover and must not set this one's angle.
    [Fact]
    public void Only_nodes_inside_the_place_count()
    {
        var outside = new[] { 30f, 30f, 30f }.Select(yaw => (new WorldPoint(50, 0, 50), yaw));

        Assert.Equal(0, MapViews.YawOf(Store, outside));
    }

    [Theory]
    [InlineData(105, 15)]
    [InlineData(-75, 15)]
    [InlineData(45, -45)]
    [InlineData(-46, 44)]
    public void An_angle_folds_into_a_quarter_turn(float yaw, float folded)
    {
        Assert.Equal(folded, MapViews.Fold(yaw), 3);
    }
}

public class AutoViewsTests
{
    // The game needs a moment after arriving to draw the place; a picture on the first frame would be empty.
    [Fact]
    public void A_place_is_due_once_the_view_has_stayed_on_it()
    {
        var auto = new AutoViews();

        Assert.False(auto.IsDue("Town/Market", 0));
        Assert.False(auto.IsDue("Town/Market", AutoViews.SettleSeconds - 1));
        Assert.True(auto.IsDue("Town/Market", AutoViews.SettleSeconds));
    }

    [Fact]
    public void Moving_to_another_place_starts_the_wait_again()
    {
        var auto = new AutoViews();

        auto.IsDue("Town/Home", 0);
        auto.IsDue("Town/Market", 4);

        Assert.False(auto.IsDue("Town/Market", AutoViews.SettleSeconds + 1));
        Assert.True(auto.IsDue("Town/Market", AutoViews.SettleSeconds + 4));
    }

    [Fact]
    public void A_good_picture_is_taken_once_a_session()
    {
        var auto = new AutoViews();

        auto.IsDue("Town/Home", 0);
        auto.Record("Town/Home", isGood: true, 5);

        Assert.False(auto.IsDue("Town/Home", 100));
    }

    // An empty picture may be a paused or still-loading screen, so it is tried again, but not forever.
    [Fact]
    public void An_empty_picture_is_tried_again_later_a_few_times()
    {
        var auto = new AutoViews();
        var now = 0f;

        auto.IsDue("Town/Home", now);
        now = AutoViews.SettleSeconds;

        for (var tries = 0; tries < AutoViews.MaxTries; tries++)
        {
            Assert.True(auto.IsDue("Town/Home", now));
            auto.Record("Town/Home", isGood: false, now);
            Assert.False(auto.IsDue("Town/Home", now + 1));
            now += AutoViews.RetrySeconds;
        }

        Assert.False(auto.IsDue("Town/Home", now + 1000));
        Assert.True(auto.HasGivenUp("Town/Home"));
    }

    [Fact]
    public void Nothing_on_view_is_never_due()
    {
        var auto = new AutoViews();

        auto.IsDue(null, 0);

        Assert.False(auto.IsDue(null, 100));
    }
}
