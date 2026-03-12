// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EUVA.Core.Services;

public sealed class AiClient : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(200) };

    public async Task<string> RequestRenamesAsync(string apiKey, string systemPrompt, string miniIr, string baseUrl, string modelName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
        
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
          
            var sanitizedKey = new string(apiKey.Where(c => c <= 127).ToArray()).Trim();
            if (!string.IsNullOrEmpty(sanitizedKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sanitizedKey);
            }
        }
    

        var payload = new
        {
            model = modelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = miniIr }
            },
            temperature = 0.0
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"AI Provider returned {response.StatusCode}: {content}");
        }

        using var doc = ParseJsonSafely(content, "Universal Client");
        
       
        try
        {
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new Exception($"Response format incompatible with OpenAI protocol. Error: {ex.Message}. Content: {content}");
        }
    }

    private JsonDocument ParseJsonSafely(string content, string providerName)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new Exception($"{providerName} returned an empty response.");
        }

        string trimmed = content.Trim();
        if (trimmed.StartsWith("<"))
        {
            string snippet = trimmed.Length > 200 ? trimmed.Substring(0, 200) + "..." : trimmed;
            throw new Exception($"{providerName} returned HTML instead of JSON. This usually indicates a network error, proxy block, or incorrect API URL.\n\nResponse Snippet: {snippet}");
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            string snippet = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
            throw new JsonException($"Failed to parse {providerName} response as JSON: {ex.Message}\n\nContent received: {snippet}", ex);
        }
    }

    public void Dispose() => _client.Dispose();
}
