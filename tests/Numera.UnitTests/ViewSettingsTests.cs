using Numera.Application.Metrics;
using Numera.Application.Views;

namespace Numera.UnitTests;

public class ViewSettingsTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static ViewSettings Settings(string? preset = "custom", string? from = "2026-07-01", string? to = "2026-09-30",
        string[]? regions = null, string? granularity = "week", string? compare = "previous") =>
        new(preset, from, to, regions ?? ["Europe"], [], [], granularity, compare);

    private static ViewSettings Valid(ViewSettings settings)
    {
        var errors = new Dictionary<string, string[]>();
        var result = ViewSettingsRules.Validate(settings, errors);
        Assert.Empty(errors);
        return result!;
    }

    [Fact]
    public void Valid_settings_are_cleaned_and_keep_their_meaning()
    {
        var cleaned = Valid(new ViewSettings(" Custom ", "2026-07-01", "2026-09-30", [" Europe ", "Europe", ""], null, null, "WEEK", "Year"));

        Assert.Equal("custom", cleaned.Preset);
        Assert.Equal(["Europe"], cleaned.Regions!);
        Assert.Empty(cleaned.Channels!);
        Assert.Equal(("week", "year"), (cleaned.Granularity, cleaned.Compare));
    }

    [Fact]
    public void Defaults_are_all_time_by_month_without_comparison_and_dates_are_dropped_for_a_preset()
    {
        var cleaned = Valid(new ViewSettings(null, "2026-07-01", "2026-09-30", null, null, null, null, null));
        Assert.Equal(("all", (string?)null, (string?)null, "month", "none"), (cleaned.Preset, cleaned.From, cleaned.To, cleaned.Granularity, cleaned.Compare));
        Assert.Empty(cleaned.Regions!);
    }

    [Fact]
    public void Every_bad_setting_is_reported_with_the_dashboard_rules()
    {
        var errors = new Dictionary<string, string[]>();

        var result = ViewSettingsRules.Validate(
            new ViewSettings("lastweek", "2026-10-01", "2026-09-01", [new string('x', 61)], null, null, "year", "quarter"), errors);

        Assert.Null(result);
        Assert.Equal(["settings.compare", "settings.granularity", "settings.preset", "settings.region"], errors.Keys.Order());
    }

    [Fact]
    public void Custom_dates_in_the_wrong_order_are_refused()
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(ViewSettingsRules.Validate(Settings(from: "2026-10-01", to: "2026-09-01"), errors));
        Assert.Contains("settings.to", errors.Keys);
    }

    [Fact]
    public void Missing_settings_are_refused()
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(ViewSettingsRules.Validate(null, errors));
        Assert.Equal(["The view settings are missing."], errors["settings"]);
    }

    [Theory]
    [InlineData("last30", "2026-10-18", "2026-11-16")]
    [InlineData("last90", "2026-08-19", "2026-11-16")]
    [InlineData("ytd", "2026-01-01", "2026-11-16")]
    [InlineData("custom", "2026-07-01", "2026-09-30")]
    public void Presets_stay_relative_and_end_today(string preset, string from, string to)
    {
        var resolved = ViewSettingsRules.Resolve(Valid(Settings(preset: preset)), Today);

        Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to)), (resolved.Filter.From!.Value, resolved.Filter.To!.Value));
        Assert.Equal(["Europe"], resolved.Filter.Regions);
        Assert.Equal(Granularity.Week, resolved.Granularity);
        Assert.Equal(ComparisonMode.Previous, resolved.Comparison!.Mode);
    }

    [Fact]
    public void All_time_and_open_ranges_have_no_comparison_as_on_the_dashboard()
    {
        Assert.Null(ViewSettingsRules.Resolve(Valid(Settings(preset: "all")), Today).Comparison);
        var open = ViewSettingsRules.Resolve(Valid(Settings(to: null)), Today);
        Assert.Equal((DateOnly.Parse("2026-07-01"), (DateOnly?)null), (open.Filter.From!.Value, open.Filter.To));
        Assert.Null(open.Comparison);
        Assert.Null(ViewSettingsRules.Resolve(Valid(Settings(compare: "none")), Today).Comparison);
    }

    [Fact]
    public void Tokens_are_43_url_safe_characters_different_every_time_and_stored_only_as_a_sha256_hash()
    {
        var (token, hash) = ShareTokens.Create();
        var (other, _) = ShareTokens.Create();

        Assert.Equal(43, token.Length);
        Assert.True(ShareTokens.LooksValid(token));
        Assert.NotEqual(token, other);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, ShareTokens.Hash(token));
        Assert.DoesNotContain(token, hash);
        // A known value, so the hash function cannot change silently: SHA-256 of "abc".
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ShareTokens.Hash("abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")] // 43 characters, but '+' is not URL-safe base64
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 44 characters
    public void Malformed_tokens_are_rejected_before_any_lookup(string? token) => Assert.False(ShareTokens.LooksValid(token));
}
