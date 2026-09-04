using System.ComponentModel.DataAnnotations;

namespace FindThatBook.Api.Configuration;

/// <summary>
/// Settings for the Gemini call that interprets a raw user query, bound from the "Gemini"
/// configuration section.
/// </summary>
public sealed class GeminiOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Gemini";

    /// <summary>
    /// Gemini API key. Deliberately optional and deliberately absent from appsettings.json:
    /// supply it through user secrets or the GEMINI_API_KEY environment variable. When it is
    /// missing the app still runs, and query extraction uses the deterministic parser instead.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>Model id, for example "gemini-2.5-flash".</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Gemini:Model must be configured.")]
    public string Model { get; init; } = string.Empty;

    /// <summary>Base address of the Generative Language API, including the version segment.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Gemini:BaseUrl must be configured.")]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Per-request timeout for the extraction call.</summary>
    [Range(1, 120, ErrorMessage = "Gemini:TimeoutSeconds must be configured between 1 and 120.")]
    public int TimeoutSeconds { get; init; }

    /// <summary>True when an API key is present and the model can actually be called.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
