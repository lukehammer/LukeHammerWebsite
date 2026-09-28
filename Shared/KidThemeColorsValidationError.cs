using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared
{
    public sealed class KidThemeColorsValidationError
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;

        [JsonPropertyName("issuesByKid")]
        public Dictionary<string, KidThemeColorValidationIssue> IssuesByKid { get; set; } =
            new Dictionary<string, KidThemeColorValidationIssue>(StringComparer.Ordinal);

        public static bool TryParse(string body, out KidThemeColorsValidationError? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith("{"))
            {
                return false;
            }

            try
            {
                error = JsonSerializer.Deserialize<KidThemeColorsValidationError>(body, KidThemeColors.JsonOptions);
                return error != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
