using System.Text.Json.Serialization;

namespace Blog.Web.Services;

public class ReCaptchaService
{
    private readonly HttpClient _http;
    private readonly string _secretKey;

    public ReCaptchaService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _secretKey = config["ReCaptcha:SecretKey"] ?? string.Empty;
    }

    public async Task<bool> ValidateAsync(string? token)
    {
        // If not configured, skip validation (safe default for development)
        if (string.IsNullOrWhiteSpace(_secretKey))
            return true;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var response = await _http.PostAsync(
            "https://www.google.com/recaptcha/api/siteverify",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("secret", _secretKey),
                new KeyValuePair<string, string>("response", token)
            ]));

        var result = await response.Content.ReadFromJsonAsync<ReCaptchaResult>();
        return result?.Success == true;
    }
}

file sealed class ReCaptchaResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; set; }
}
