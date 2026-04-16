using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LabelPrintClient.Services;

public class PrintServerClient : IDisposable
{
    private readonly HttpClient _http;
    private string _baseUrl = "";

    public PrintServerClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public void SetBaseUrl(string serverAddress)
    {
        var addr = serverAddress.Trim().TrimEnd('/');
        if (!addr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !addr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            addr = "http://" + addr;
        }
        _baseUrl = addr;
    }

    public async Task<bool> CheckHealthAsync()
    {
        if (string.IsNullOrEmpty(_baseUrl))
            return false;

        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<HealthStatus> CheckDetailedHealthAsync()
    {
        if (string.IsNullOrEmpty(_baseUrl))
            return HealthStatus.Offline("Server address not configured");

        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/health");
            if (!response.IsSuccessStatusCode)
                return HealthStatus.Offline($"Server returned HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();

            bool p750w = true;
            bool p300bt = true;

            try
            {
                var root = JsonDocument.Parse(body).RootElement;

                if (root.TryGetProperty("p750w", out var p1))
                    p750w = p1.GetString() == "ok";
                if (root.TryGetProperty("p300bt", out var p2))
                    p300bt = p2.GetString() == "ok";
            }
            catch
            {
                // Response isn't JSON or has no printer fields — server is up, assume both available
            }

            return new HealthStatus(true, p750w, p300bt, $"Response: {body}");
        }
        catch (Exception ex)
        {
            return HealthStatus.Offline(ex.Message);
        }
    }

    public async Task<PrintResult> PrintAsync(string printer, string text)
    {
        if (string.IsNullOrEmpty(_baseUrl))
            return new PrintResult(false, "Server address not configured.");

        try
        {
            var payload = JsonSerializer.Serialize(new { text });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_baseUrl}/api/print/{printer}", content);

            if (response.IsSuccessStatusCode)
                return new PrintResult(true, null);

            var body = await response.Content.ReadAsStringAsync();
            return new PrintResult(false, $"Server returned {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new PrintResult(false, ex.Message);
        }
    }

    public void Dispose() => _http.Dispose();
}

public record PrintResult(bool Success, string? Error);

public record HealthStatus(bool ServerOnline, bool P750wAvailable, bool P300btAvailable, string DiagInfo)
{
    public static HealthStatus Offline(string reason) => new(false, false, false, reason);
}
