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

    /// <summary>
    /// Budget for one logical model call, covering every retry attempt rather than each one.
    /// </summary>
    /// <remarks>
    /// Same bounds as OpenLibrary:TotalTimeoutSeconds, and for the same reasons. It must
    /// exceed the resilience handler's 20 second attempt timeout, or a single slow attempt
    /// consumes the whole budget and our own cancellation kills the retry that would have
    /// succeeded -- which is exactly what a transient 503 from Gemini produces. It must stay
    /// under the handler's 60 second total so a timeout surfaces through our cancellation
    /// path. Both are set in ServiceDefaults.
    /// </remarks>
    [Range(21, 59, ErrorMessage = "Gemini:TimeoutSeconds must be configured between 21 and 59.")]
    public int TimeoutSeconds { get; init; }

    /// <summary>True when an API key is present and the model can actually be called.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
