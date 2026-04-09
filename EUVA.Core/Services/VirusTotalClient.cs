// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace EUVA.Core.Services;

public sealed class VirusTotalClient : IDisposable
{
    private const string BaseUrl = "https://www.virustotal.com/api/v3/files/";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _apiKey;
    private readonly Action<string, string, string?>? _log; 
    private string? _currentSha256;

    public VirusTotalClient(string apiKey, Action<string, string, string?>? log = null)
    {
        _apiKey = apiKey;
        _log = log;
    }

    public static string ComputeSha256(string filePath)
    {
        using var fs = new System.IO.FileStream(filePath, 
            System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(fs);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private void Log(string message, string color, string? url = null) 
    {
        _log?.Invoke(message, color, url);
    }

    public async Task QueryHashAsync(string sha256)
    {
        _currentSha256 = sha256;
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            Log("[VT] API key not configured. Skipping scan.", "#F9E2AF");
            return;
        }

        Log($"[VT] Querying VirusTotal for {sha256[..12]}...", "#89B4FA");

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + sha256);
            request.Headers.Add("x-apikey", _apiKey);

            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Log("[VT] File not found in VirusTotal database (clean or unknown).", "#A6E3A1");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                Log($"[VT] API error: {response.StatusCode}", "#F38BA8");
                return;
            }

            ParseAndLog(body);
        }
        catch (TaskCanceledException)
        {
            Log("[VT] Request timed out.", "#F9E2AF");
        }
        catch (Exception ex)
        {
            Log($"[VT] Error: {ex.Message}", "#F38BA8");
        }
    }

    private void ParseAndLog(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var attrs = doc.RootElement.GetProperty("data").GetProperty("attributes");

        var stats = attrs.GetProperty("last_analysis_stats");
        int malicious   = stats.GetProperty("malicious").GetInt32();
        int suspicious  = stats.GetProperty("suspicious").GetInt32();
        int undetected  = stats.GetProperty("undetected").GetInt32();
        int harmless    = stats.GetProperty("harmless").GetInt32();
        int total       = malicious + suspicious + undetected + harmless;

        string color;
        if (malicious == 0 && suspicious == 0)
            color = "#A6E3A1"; 
        else if (malicious <= 5)
            color = "#F9E2AF"; 
        else
            color = "#F38BA8"; 

        Log($"[VT] Detection: {malicious}/{total} engines flagged as malicious, {suspicious} suspicious.", color);

        string reportUrl = $"https://www.virustotal.com/gui/file/{_currentSha256}/detection";
        Log("[Report]", "#89B4FA", reportUrl);

        if (attrs.TryGetProperty("type_description", out var typeDesc))
           Log($"\n[VT] Type: {typeDesc.GetString()}", "#CDD6F4");

        if (attrs.TryGetProperty("popular_threat_classification", out var threatClass))
        {
            if (threatClass.TryGetProperty("suggested_threat_label", out var label))
                Log($"[VT] Threat label: {label.GetString()}", color);
        }

        if (attrs.TryGetProperty("names", out var names) && names.GetArrayLength() > 0)
        {
            var firstName = names[0].GetString();
            Log($"[VT] Known as: {firstName}", "#CDD6F4");
        }

        if (attrs.TryGetProperty("packers", out var packers))
        {
            foreach (var packer in packers.EnumerateObject())
            {
                Log($"[VT] Packer detected: {packer.Value.GetString()} (by {packer.Name})", "#F9E2AF");
            }
        }

        if (attrs.TryGetProperty("signature_info", out var sigInfo))
        {
            if (sigInfo.TryGetProperty("subject", out var subject))
                Log($"[VT] Signed by: {subject.GetString()}", "#94E2D5");
        }
    }

    public void Dispose() => _http.Dispose();
}
