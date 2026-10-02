using MovieAssistant.Api.Models;

namespace MovieAssistant.Api.Tools;

internal static class MovieFilter
{
    public static IEnumerable<Movie> Apply(
        IEnumerable<Movie> movies,
        string? genre,
        int? yearFrom,
        int? yearTo,
        double? minimumRating,
        string? director,
        string? castMember,
        string? title = null)
    {
        var results = movies;

        if (title is not null)
            results = results.Where(m =>
                m.Title.Contains(title, StringComparison.OrdinalIgnoreCase));

        if (genre is not null)
            results = results.Where(m =>
                m.Genres.Any(g => g.Equals(genre, StringComparison.OrdinalIgnoreCase)));

        if (yearFrom is not null)
            results = results.Where(m => m.Year >= yearFrom);

        if (yearTo is not null)
            results = results.Where(m => m.Year <= yearTo);

        if (minimumRating is not null)
            results = results.Where(m => m.Rating >= minimumRating);

        if (director is not null)
            results = results.Where(m =>
                m.Director.Contains(director, StringComparison.OrdinalIgnoreCase));

        if (castMember is not null)
            results = results.Where(m =>
                m.Cast.Any(c => c.Contains(castMember, StringComparison.OrdinalIgnoreCase)));

        return results;
    }
}
