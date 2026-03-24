using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MCP_MI.Samples;

public static class IcmMcpClientSample
{
    public static async Task RunAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>();

        using var loggerFactory = LoggerFactory.Create(builder => { });

        ILogger logger = loggerFactory.CreateLogger("IcmMcpClientSample");

        // Connect to an MCP server
        Console.WriteLine("Connecting client to MCP server");

        // Add server token authentication to the client.
        DefaultAzureCredential credential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { ManagedIdentityClientId = "3bc62a4d-a65e-48ed-af39-f70577ab184c" });

        AccessToken accessToken = credential.GetToken(new TokenRequestContext(new[] { "api://icmmcpapi-ppe" }));
        var token = accessToken.Token;
        var icmAppId = Environment.GetEnvironmentVariable("ICM_APP_ID"); // Optional bulk-access app id

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://icm-mcp-ppe.azure-api.net/v1/")
        };
        httpClient.Timeout = new TimeSpan(0, 0, 100);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrWhiteSpace(icmAppId))
        {
            httpClient.DefaultRequestHeaders.Add("x-icm-appid", icmAppId);
        }

        // Get all available tools
        Console.WriteLine("Available Tools list from IcM MCP Server:");

        IList<ToolDoc> tools = new List<ToolDoc>();

        try
        {
            JsonElement listResult = await SendMcpRequestAsync(
                httpClient,
                method: "tools/list",
                @params: new { });

            var toolList = new List<ToolDoc>();
            if (listResult.TryGetProperty("tools", out JsonElement toolsElement) && toolsElement.ValueKind == JsonValueKind.Array)
            {
                int idx = 1;
                foreach (JsonElement tool in toolsElement.EnumerateArray())
                {
                    string name = tool.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                    string description = tool.TryGetProperty("description", out JsonElement descEl) ? descEl.GetString() ?? string.Empty : string.Empty;
                    string parameters = ExtractToolParameters(tool);
                    string exampleArguments = BuildExampleArguments(tool);
                    toolList.Add(new ToolDoc(idx, name, description, parameters, exampleArguments));
                    idx++;
                }
            }

            tools = toolList;
            foreach (var tool in tools)
            {
                Console.WriteLine($"{tool.Number}.{tool.Name}");
                Console.WriteLine($"Description: {tool.Description}");
            }

            WriteToolsMarkdown(tools, Path.Combine("docs", "icm-mcp-tools.md"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while listing tools.");
        }

        Console.WriteLine("======================================================");

        Console.WriteLine("Call tool to get incident details for incident 139979555");

        JsonElement callResult = await SendMcpRequestAsync(
            httpClient,
            method: "tools/call",
            @params: new
            {
                name = "get_incident_details_by_id",
                arguments = new Dictionary<string, object?>() { ["incidentId"] = 139979555 }
            });

        Console.WriteLine(ExtractFirstTextContent(callResult));
        Console.WriteLine("Call tool to get contact for alias yudihe");

        callResult = await SendMcpRequestAsync(
            httpClient,
            method: "tools/call",
            @params: new
            {
                name = "get_contact_by_alias",
                arguments = new Dictionary<string, object?>() { ["alias"] = "yudihe" }
            });

        Console.WriteLine(ExtractFirstTextContent(callResult));
    }

    private static async Task<JsonElement> SendMcpRequestAsync(HttpClient client, string method, object @params)
    {
        var requestBody = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method,
            @params
        };

        string json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(string.Empty, content);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType?.Contains("event-stream", StringComparison.OrdinalIgnoreCase) == true)
        {
            responseJson = ExtractJsonFromSse(responseJson);
        }

        using JsonDocument doc = JsonDocument.Parse(responseJson);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("error", out JsonElement errorElement))
        {
            throw new InvalidOperationException($"MCP error: {errorElement}");
        }

        if (!root.TryGetProperty("result", out JsonElement resultElement))
        {
            throw new InvalidOperationException("MCP response missing 'result'.");
        }

        return resultElement.Clone();
    }

    private static string ExtractJsonFromSse(string ssePayload)
    {
        string? lastJson = null;
        string[] lines = ssePayload.Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string data = line.Substring("data:".Length).Trim();
            if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
            {
                continue;
            }

            if (data.StartsWith("{"))
            {
                lastJson = data;
            }
        }

        if (string.IsNullOrWhiteSpace(lastJson))
        {
            throw new InvalidOperationException("No JSON data found in SSE response.");
        }

        return lastJson;
    }

    private static string ExtractFirstTextContent(JsonElement callResult)
    {
        if (callResult.TryGetProperty("content", out JsonElement contentArray) && contentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in contentArray.EnumerateArray())
            {
                string type = item.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() ?? string.Empty : string.Empty;
                if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("text", out JsonElement textEl))
                {
                    return textEl.GetString() ?? string.Empty;
                }
            }
        }

        return callResult.ToString();
    }

    private static string ExtractToolParameters(JsonElement tool)
    {
        if (!tool.TryGetProperty("inputSchema", out JsonElement inputSchema) || inputSchema.ValueKind != JsonValueKind.Object)
        {
            return "None";
        }

        if (!inputSchema.TryGetProperty("properties", out JsonElement properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return "None";
        }

        var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inputSchema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement req in required.EnumerateArray())
            {
                string? reqName = req.GetString();
                if (!string.IsNullOrWhiteSpace(reqName))
                {
                    requiredSet.Add(reqName);
                }
            }
        }

        var parts = new List<string>();
        foreach (JsonProperty prop in properties.EnumerateObject())
        {
            string paramName = prop.Name;
            JsonElement paramSpec = prop.Value;
            string paramType = paramSpec.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() ?? "any" : "any";
            bool isRequired = requiredSet.Contains(paramName);
            parts.Add($"{paramName}:{paramType}{(isRequired ? " (required)" : string.Empty)}");
        }

        if (parts.Count == 0)
        {
            return "None";
        }

        return string.Join(", ", parts);
    }

    private static string BuildExampleArguments(JsonElement tool)
    {
        if (!tool.TryGetProperty("inputSchema", out JsonElement inputSchema) || inputSchema.ValueKind != JsonValueKind.Object)
        {
            return "{}";
        }

        if (!inputSchema.TryGetProperty("properties", out JsonElement properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return "{}";
        }

        var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inputSchema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement req in required.EnumerateArray())
            {
                string? reqName = req.GetString();
                if (!string.IsNullOrWhiteSpace(reqName))
                {
                    requiredSet.Add(reqName);
                }
            }
        }

        var example = new Dictionary<string, object?>();
        foreach (JsonProperty prop in properties.EnumerateObject())
        {
            if (!requiredSet.Contains(prop.Name))
            {
                continue;
            }

            string paramType = prop.Value.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() ?? "string" : "string";
            example[prop.Name] = GetSampleValue(paramType, prop.Name);
        }

        return JsonSerializer.Serialize(example);
    }

    private static object? GetSampleValue(string type, string name)
    {
        string lowerName = name.ToLowerInvariant();
        if (lowerName.Contains("incidentid") || lowerName.EndsWith("id", StringComparison.OrdinalIgnoreCase))
        {
            return 139979555;
        }

        return type switch
        {
            "integer" => 1,
            "number" => 1,
            "boolean" => true,
            "array" => Array.Empty<object>(),
            "object" => new { },
            _ => "sample"
        };
    }

    private static void WriteToolsMarkdown(IList<ToolDoc> tools, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        var sb = new StringBuilder();
        sb.AppendLine("# IcM MCP Tools");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine($"Total tools: {tools.Count}");
        sb.AppendLine();
        sb.AppendLine("| # | Tool | Description | Parameters | Example arguments (JSON) |");
        sb.AppendLine("|---:|---|---|---|---|");

        foreach (ToolDoc tool in tools)
        {
            string name = EscapeForTable(tool.Name);
            string description = EscapeForTable(tool.Description);
            string parameters = EscapeForTable(tool.Parameters);
            string args = EscapeForTable(tool.ExampleArguments);
            sb.AppendLine($"| {tool.Number} | {name} | {description} | {parameters} | {args} |");
        }

        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"Saved tools documentation to {path}");
    }

    private static string EscapeForTable(string value)
    {
        return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private sealed record ToolDoc(int Number, string Name, string Description, string Parameters, string ExampleArguments);
}
