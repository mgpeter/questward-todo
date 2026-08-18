using System.Globalization;
using System.Text.RegularExpressions;

namespace TodoApp.Tests.Design;

/// <summary>
/// The contrast check DEC-009 asked for and never had.
/// </summary>
/// <remarks>
/// DEC-009 says colour in this app is "validated, not eyeballed", but the validator it refers
/// to was external and one-off, and it covered only the four difficulty hues. The neutral ramp
/// was never in scope, and it drifted: the first dark palette put <c>--ink-faint</c> at 3.40:1
/// on a panel while 178 call sites used it, nearly all at 10.5-11px. That is ordinary text far
/// below the size where 3:1 is allowed, so it failed AA outright, and the product owner found
/// it by looking at the screen.
/// <para>
/// Reading the stylesheet rather than a duplicated table is the whole point. A copy of the
/// palette here would be a second source of truth that passes while the app ships something
/// else, which is exactly the failure this is meant to catch.
/// </para>
/// </remarks>
public class ContrastTests
{
    /// <summary>Text at normal size, per WCAG 1.4.3.</summary>
    private const double TextAa = 4.5;

    /// <summary>Interactive boundaries, per WCAG 1.4.11 non-text contrast.</summary>
    private const double NonText = 3.0;

    private static readonly Lazy<(IReadOnlyDictionary<string, string> Light, IReadOnlyDictionary<string, string> Dark)>
        Palette = new(ReadPalette);

    public static TheoryData<string, string, double> TextTokens() => new()
    {
        // token, theme, floor
        { "--ink", "light", 7.0 },
        { "--ink", "dark", 7.0 },
        { "--ink-muted", "light", TextAa },
        { "--ink-muted", "dark", TextAa },

        // The one that failed. Held to the text floor and not the non-text one, because it is
        // used at 10.5px and 11px throughout: column headings, empty-column prompts, XP
        // figures, the tag and difficulty filter chips.
        { "--ink-faint", "light", TextAa },
        { "--ink-faint", "dark", TextAa },
    };

    [Theory]
    [MemberData(nameof(TextTokens))]
    public void Every_text_token_is_readable_on_a_panel(string token, string theme, double floor)
    {
        var palette = theme == "dark" ? Palette.Value.Dark : Palette.Value.Light;

        var actual = Contrast(palette[token], palette["--surface"]);

        Assert.True(
            actual >= floor,
            $"{token} in {theme} is {actual:F2}:1 on --surface ({palette["--surface"]}), " +
            $"below the {floor:F1}:1 floor. Lighten it in web/src/index.css.");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void The_interactive_border_carries_non_text_contrast(string theme)
    {
        var palette = theme == "dark" ? Palette.Value.Dark : Palette.Value.Light;

        var actual = Contrast(palette["--line-strong"], palette["--surface"]);

        Assert.True(
            actual >= NonText,
            $"--line-strong in {theme} is {actual:F2}:1, below {NonText:F1}:1. It draws the " +
            "checkbox ring and the scrollbar thumb, which are controls rather than decoration.");
    }

    /// <summary>
    /// The decorative border is deliberately held to a floor well under 3:1.
    /// </summary>
    /// <remarks>
    /// <c>index.css</c> carries a global <c>* { border-color: var(--line) }</c>, so this token
    /// draws every edge in the application. Taking it to the non-text 3:1 would be defensible
    /// by the letter of WCAG and would make the app read as a wireframe. The floor here only
    /// asserts the edge is visible at all, which is what it was not.
    /// </remarks>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void The_decorative_border_is_visible_without_shouting(string theme)
    {
        var palette = theme == "dark" ? Palette.Value.Dark : Palette.Value.Light;

        var actual = Contrast(palette["--line"], palette["--surface"]);

        Assert.True(actual >= 1.6, $"--line in {theme} is {actual:F2}:1; a panel edge should be visible.");
        Assert.True(actual < NonText, $"--line in {theme} is {actual:F2}:1; that is loud enough to read as a wireframe.");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void A_panel_separates_from_the_page_behind_it(string theme)
    {
        var palette = theme == "dark" ? Palette.Value.Dark : Palette.Value.Light;

        var actual = Contrast(palette["--surface"], palette["--canvas"]);

        Assert.True(
            actual >= 1.12,
            $"--surface and --canvas in {theme} are {actual:F2}:1 apart. A panel that does not " +
            "separate from the page leaves the card shadow doing all the work.");
    }

    /// <summary>
    /// The tier hues are DEC-009's territory, so this asserts only that they stayed legible as
    /// small chip text and does not second-guess the hue separation the validator settled.
    /// </summary>
    [Theory]
    [InlineData("--tier-easy")]
    [InlineData("--tier-medium")]
    [InlineData("--tier-hard")]
    [InlineData("--tier-epic")]
    public void Every_difficulty_chip_stays_legible_in_dark(string token)
    {
        var palette = Palette.Value.Dark;

        var actual = Contrast(palette[token], palette["--surface"]);

        Assert.True(actual >= NonText, $"{token} is {actual:F2}:1 as chip text on a dark panel.");
    }

    // ----------------------------------------------------------------- machinery

    private static double Contrast(string a, string b)
    {
        var (high, low) = (Luminance(a), Luminance(b)) switch
        {
            var (x, y) when x >= y => (x, y),
            var (x, y) => (y, x)
        };

        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance(string hex)
    {
        var value = hex.TrimStart('#');

        var channels = Enumerable.Range(0, 3)
            .Select(i => int.Parse(value.Substring(i * 2, 2), NumberStyles.HexNumber))
            .Select(Channel)
            .ToArray();

        return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
    }

    private static double Channel(int eightBit)
    {
        var c = eightBit / 255.0;

        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static (IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>) ReadPalette()
    {
        var css = File.ReadAllText(FindStylesheet());

        // The light palette is on :root and the dark one overrides it in a .dark block, so the
        // dark theme is the light one with the overrides applied rather than a whole palette.
        var light = TokensIn(css, ":root");
        var dark = new Dictionary<string, string>(light, StringComparer.Ordinal);

        foreach (var (token, value) in TokensIn(css, ".dark"))
        {
            dark[token] = value;
        }

        Assert.True(light.Count > 0, "No tokens parsed from :root; has the stylesheet moved?");
        Assert.True(dark.Count > 0, "No tokens parsed from .dark; has the stylesheet moved?");

        return (light, dark);
    }

    private static Dictionary<string, string> TokensIn(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find the '{selector}' block in index.css.");

        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        var block = css[(open + 1)..close];

        // Hex only. rgb() and color-mix() values exist in the file but none of the tokens
        // under test use them, and a half-parsed colour would be worse than an absent one.
        return Regex.Matches(block, @"(--[a-z-]+):\s*(#[0-9a-fA-F]{6})\s*;")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Walks up from the test binary to the repository root. The alternative, a relative path
    /// from the working directory, differs between "dotnet test" and an IDE runner.
    /// </summary>
    private static string FindStylesheet()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "web", "src", "index.css");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not find web/src/index.css by walking up from " + AppContext.BaseDirectory);
    }
}
