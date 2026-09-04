using System.Text.Json.Serialization;

namespace FindThatBook.Api.Models.DTO;

/// <summary>
/// Which path produced the interpretation of a query. Surfaced to clients so a UI can say
/// plainly that AI interpretation was unavailable rather than silently degrading.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<QueryExtractionSource>))]
public enum QueryExtractionSource
{
    /// <summary>Interpreted by the language model.</summary>
    Llm,

    /// <summary>Interpreted by the built-in deterministic parser.</summary>
    Fallback
}
