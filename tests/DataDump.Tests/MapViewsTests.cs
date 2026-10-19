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

        Assert.Equal(247, cover.Max.X);
        Assert.Equal(200, cover.Min.X);
    }

    // An expedition's one camera area can be a small part of the building; its picture must hold every spot.
    [Fact]
    public void The_one_area_of_a_map_grows_to_hold_every_node()
    {
        var nodes = new[] { At(30, -100), At(70, -60) };
        var cover = MapViews.Cover(new MapArea("School", At(53, -86), At(77, -68)), 1, nodes);

        Assert.Equal((30f, -100f, 77f, -60f), (cover.Min.X, cover.Min.Z, cover.Max.X, cover.Max.Z));
    }

    [Fact]
    public void A_picture_of_only_black_is_blank()
    {
        Assert.True(MapViews.IsBlank(new (byte, byte, byte)[] { (0, 0, 0), (1, 2, 0) }));
        Assert.False(MapViews.IsBlank(new (byte, byte, byte)[] { (0, 0, 0), (0, 0, 40) }));
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

        Assert.False(auto.IsDue("Town/Market", 6));
        Assert.True(auto.IsDue("Town/Market", 9));
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
