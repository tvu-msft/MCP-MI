using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MCP_MI.FunctionAppSample;

public class McpCallerFunction
{
    private readonly ILogger<McpCallerFunction> _logger;

    public McpCallerFunction(ILogger<McpCallerFunction> logger)
    {
        _logger = logger;
    }

    [Function("CallMcp")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        try
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            using JsonDocument inputDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(requestBody) ? "{}" : requestBody);
            JsonElement inputRoot = inputDoc.RootElement;

            string toolName = inputRoot.TryGetProperty("toolName", out JsonElement toolEl)
                ? toolEl.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(toolName))
            {
                HttpResponseData bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("Missing required field: toolName");
                return bad;
            }

            JsonElement args = inputRoot.TryGetProperty("arguments", out JsonElement argsEl)
                ? argsEl.Clone()
                : JsonDocument.Parse("{}").RootElement.Clone();

            string endpoint = Environment.GetEnvironmentVariable("MCP_ENDPOINT")
                ?? "https://icm-mcp-prod.azure-api.net/v1/";

            string scope = Environment.GetEnvironmentVariable("MCP_SCOPE")
                ?? "api://icmmcpapi-prod/.default";

            string? managedIdentityClientId = Environment.GetEnvironmentVariable("MANAGED_IDENTITY_CLIENT_ID");
            string? icmAppId = Environment.GetEnvironmentVariable("ICM_APP_ID");

            TokenCredential credential = string.IsNullOrWhiteSpace(managedIdentityClientId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = managedIdentityClientId
                });

            // Acquire AAD token for MCP API.
            AccessToken token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                CancellationToken.None);

            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(endpoint),
                Timeout = TimeSpan.FromSeconds(100)
            };

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            if (!string.IsNullOrWhiteSpace(icmAppId))
            {
                httpClient.DefaultRequestHeaders.Add("x-icm-appid", icmAppId);
            }

            var payload = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString("N"),
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments = JsonSerializer.Deserialize<object>(args.GetRawText())
                }
            };

            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var sw = Stopwatch.StartNew();
            using HttpResponseMessage upstream = await httpClient.PostAsync(string.Empty, content);
            sw.Stop();

            string upstreamBody = await upstream.Content.ReadAsStringAsync();

            HttpResponseData response = req.CreateResponse(upstream.IsSuccessStatusCode ? HttpStatusCode.OK : HttpStatusCode.BadGateway);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");

            var output = new
            {
                toolName,
                latencyMs = sw.ElapsedMilliseconds,
                upstreamStatusCode = (int)upstream.StatusCode,
                upstreamReason = upstream.ReasonPhrase,
                mcpResponse = upstreamBody
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(output));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Function failed while calling MCP.");
            HttpResponseData error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync($"Error: {ex.Message}");
            return error;
        }
    }
}
