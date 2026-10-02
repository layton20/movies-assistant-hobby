using System.ComponentModel;
using Microsoft.Extensions.AI;
using MovieAssistant.Api.Models;

namespace MovieAssistant.Api.Tools;

public class MovieCountTool : ITool
{
    private readonly List<Movie> Movies;

    public string Name => "count_movies";

    public MovieCountTool(List<Movie> movies)
    {
        Movies = movies;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(Count, new AIFunctionFactoryOptions
    {
        Name = Name,
        Description = "Count the movies in the catalogue matching genre, release year, rating, director or cast member filters. " +
                      "Use this for 'how many' questions. Returns the total number of matches."
    });

    [Description("Count movies matching the given filters")]
    private int Count(
        [Description("Genre to filter by, e.g. 'Thriller', 'Sci-Fi', 'Horror', 'Drama', 'Comedy', 'Action'")]
        string? genre = null,

        [Description("Earliest release year to include, e.g. 2014 for 'last decade' queries")]
        int? yearFrom = null,

        [Description("Latest release year to include")]
        int? yearTo = null,

        [Description("Minimum rating on a 0–10 scale, e.g. 8.0 for highly rated movies")]
        double? minimumRating = null,

        [Description("Director name to filter by, partial match accepted")]
        string? director = null,

        [Description("Cast member name to filter by, partial match accepted")]
        string? castMember = null)
    {
        return MovieFilter.Apply(Movies, genre, yearFrom, yearTo, minimumRating, director, castMember).Count();
    }
}
