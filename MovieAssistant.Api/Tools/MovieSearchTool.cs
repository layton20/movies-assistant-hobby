using System;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using MovieAssistant.Api.Models;

namespace MovieAssistant.Api.Tools;

public class MovieSearchTool : ITool
{
    private readonly List<Movie> Movies;

    public string Name => "search_movies";

    public MovieSearchTool(List<Movie> movies)
    {
        Movies = movies;
    }

    /// <summary>
    /// Wraps this tool as an AIFunction so it can be registered with IChatClient.
    /// AIFunctionFactory reads the parameter [Description] attributes to generate
    /// the JSON schema that gets sent to the model as the tool definition.
    /// </summary>
    public AIFunction AsAIFunction() => AIFunctionFactory.Create(Search, new AIFunctionFactoryOptions
    {
        Name = Name,
        Description = "Search the movie catalogue by title, genre, release year, rating, director or cast member. " +
                      "Returns a JSON array of matching movies ordered by rating descending."
    });

    [Description("Search movies matching the given filters")]
    private string Search(
        [Description("Genre to filter by, e.g. 'Thriller', 'Sci-Fi', 'Horror', 'Drama', 'Comedy', 'Action'")]
        string? genre = null,

        [Description("Earliest release year to include, e.g. 2014 for 'last decade' queries")]
        int? yearFrom = null,

        [Description("Latest release year to include")]
        int? yearTo = null,

        [Description("Minimum rating on a 0–10 scale, e.g. 8.0 for highly rated movies")]
        double? minimumRating = null,

        [Description("Director name to filter by, partial match accepted")]
        string? director =null,

        [Description("Cast member name to filter by, partial match accepted")]
        string? castMember = null,

        [Description("Maximum number of results to return, defaults to 5, capped at 10")]
        int limit = 5,

        [Description("Number of top results to skip, for paging. Use the number of movies already shown to get 'more' results, defaults to 0")]
        int offset = 0,

        [Description("Movie title to look up, partial match accepted, e.g. 'Parasite'. Use when the user names a specific film")]
        string? title = null)
    {
        var results = MovieFilter.Apply(Movies, genre, yearFrom, yearTo, minimumRating, director, castMember, title);

        List<Movie> matched = results
            .OrderByDescending(m => m.Rating)
            .Skip(Math.Max(offset, 0))
            .Take(Math.Clamp(limit, 1, 10))
            .ToList();

        if (matched.Count == 0)
            return "No movies found matching the given criteria.";

        return JsonSerializer.Serialize(matched, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
