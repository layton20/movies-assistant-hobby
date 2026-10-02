using Microsoft.Extensions.AI;
using MovieAssistant.Api.Models;
using MovieAssistant.Api.Tools;

namespace MovieAssistant.Api.Tests;

public class MovieSearchToolTests
{
    private static readonly List<Movie> Movies =
    [
        new(3, "Parasite", 2019, ["Thriller"], 8.5, "Bong Joon-ho", ["Song Kang-ho"], "", 132),
        new(7, "Memories of Murder", 2003, ["Crime"], 8.1, "Bong Joon-ho", ["Song Kang-ho"], "", 131),
        new(9, "Heat", 1995, ["Crime"], 8.3, "Michael Mann", ["Al Pacino"], "", 170)
    ];

    private static async Task<string> Search(string title)
    {
        var result = await new MovieSearchTool(Movies).AsAIFunction()
            .InvokeAsync(new AIFunctionArguments { ["title"] = title });

        return result?.ToString() ?? "";
    }

    [Theory]
    [InlineData("Parasite")]
    [InlineData("parasite")]
    [InlineData("PARAS")]
    public async Task Title_filter_is_a_case_insensitive_partial_match(string title)
    {
        var json = await Search(title);

        Assert.Contains("\"title\":\"Parasite\"", json);
        Assert.DoesNotContain("Heat", json);
    }

    [Fact]
    public async Task Unknown_title_finds_nothing()
    {
        Assert.StartsWith("No movies found", await Search("Not A Real Film"));
    }
}
