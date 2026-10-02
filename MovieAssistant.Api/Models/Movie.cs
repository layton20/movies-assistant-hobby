using System;
using System.Text.Json.Serialization;

namespace MovieAssistant.Api.Models;

public record Movie(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("genres")] List<string> Genres,
    [property: JsonPropertyName("rating")] double Rating,
    [property: JsonPropertyName("director")] string Director,
    [property: JsonPropertyName("cast")] List<string> Cast,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("runtimeMinutes")] int RuntimeMinutes
);