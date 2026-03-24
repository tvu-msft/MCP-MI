using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MCP_MI.Samples;

public static class IcmMcpClientSample
{
    private const string IncidentIdParameterName = "incidentId";

    public static async Task RunInteractiveAsync()
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

        AccessToken accessToken = credential.GetToken(new TokenRequestContext(new[] { "api://icmmcpapi-prod" }));
        var token = accessToken.Token; // MI Token
        
        // Optional bulk-access app id 
        // Foundry / large‑scale automation often needs it
        var icmAppId = Environment.GetEnvironmentVariable("ICM_APP_ID"); 

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://icm-mcp-prod.azure-api.net/v1/")
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
                    JsonElement inputSchema = tool.TryGetProperty("inputSchema", out JsonElement schemaEl)
                        ? schemaEl.Clone()
                        : default;
                    toolList.Add(new ToolDoc(idx, name, description, parameters, exampleArguments, inputSchema));
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
            return;
        }

        while (true)
        {
            Console.WriteLine("======================================================");
            Console.WriteLine("Choose execution mode:");
            Console.WriteLine("1. Run a specific function");
            Console.WriteLine("2. Run in batch (incidentId only)");
            Console.Write("Select 1, 2, or q to quit: ");
            string? modeRaw = Console.ReadLine();

            if (string.Equals(modeRaw?.Trim(), "q", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Exiting tool runner.");
                return;
            }

            if (string.Equals(modeRaw?.Trim(), "1", StringComparison.OrdinalIgnoreCase))
            {
                await RunSpecificFlowAsync(httpClient, tools);
                continue;
            }

            if (string.Equals(modeRaw?.Trim(), "2", StringComparison.OrdinalIgnoreCase))
            {
                await RunBatchIncidentIdOnlyAsync(httpClient, tools);
                continue;
            }

            Console.WriteLine("Invalid option. Please enter 1, 2, or q.");
        }
    }

    private static async Task RunSpecificFlowAsync(HttpClient httpClient, IList<ToolDoc> tools)
    {
        while (true)
        {
            Console.WriteLine("------------------------------------------------------");
            Console.Write("Select a tool by number (or b to go back): ");
            string? selectedRaw = Console.ReadLine();

            if (string.Equals(selectedRaw?.Trim(), "b", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!int.TryParse(selectedRaw, out int selectedIndex))
            {
                Console.WriteLine("Invalid selection. Please enter a number or b.");
                continue;
            }

            ToolDoc? selectedTool = tools.FirstOrDefault(t => t.Number == selectedIndex);
            if (selectedTool is null)
            {
                Console.WriteLine("Tool not found for the selected number.");
                continue;
            }

            Dictionary<string, object?> arguments = PromptArgumentsForTool(selectedTool);

            try
            {
                Console.WriteLine($"Calling tool: {selectedTool.Name}");
                var stopwatch = Stopwatch.StartNew();
                JsonElement callResult = await SendMcpRequestAsync(
                    httpClient,
                    method: "tools/call",
                    @params: new
                    {
                        name = selectedTool.Name,
                        arguments
                    });
                stopwatch.Stop();

                Console.WriteLine("Result:");
                Console.WriteLine(ExtractFirstTextContent(callResult));
                Console.WriteLine($"Latency: {stopwatch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Tool call failed: {ex.Message}");
            }
        }
    }

    private static async Task RunBatchIncidentIdOnlyAsync(HttpClient httpClient, IList<ToolDoc> tools)
    {
        Console.Write("Enter incidentId: ");
        string? incidentIdInput = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(incidentIdInput))
        {
            Console.WriteLine("incidentId is required for batch mode.");
            return;
        }

        List<BatchRunResult> results = new List<BatchRunResult>();

        foreach (ToolDoc tool in tools)
        {
            if (!TryBuildIncidentIdOnlyArguments(tool, incidentIdInput.Trim(), out Dictionary<string, object?> arguments))
            {
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            bool success;
            string message = string.Empty;
            using var spinnerCts = new CancellationTokenSource();
            Task spinnerTask = ShowSpinnerAsync($"Running {tool.Name}", spinnerCts.Token);
            try
            {
                await SendMcpRequestAsync(
                    httpClient,
                    method: "tools/call",
                    @params: new
                    {
                        name = tool.Name,
                        arguments
                    });
                success = true;
            }
            catch (Exception ex)
            {
                success = false;
                message = BuildShortErrorMessage(ex.Message);
            }
            finally
            {
                spinnerCts.Cancel();
                try
                {
                    await spinnerTask;
                }
                catch (OperationCanceledException)
                {
                }

                stopwatch.Stop();
            }

            results.Add(new BatchRunResult(tool.Name, stopwatch.ElapsedMilliseconds, success, message));
        }

        if (results.Count == 0)
        {
            Console.WriteLine("No tools found that can run with incidentId as the only required parameter.");
            return;
        }

        PrintBatchResultsTable(results);
    }

    private static bool TryBuildIncidentIdOnlyArguments(ToolDoc tool, string incidentIdInput, out Dictionary<string, object?> arguments)
    {
        arguments = new Dictionary<string, object?>();

        if (tool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.InputSchema.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!properties.TryGetProperty(IncidentIdParameterName, out JsonElement incidentIdProperty))
        {
            return false;
        }

        var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tool.InputSchema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
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

        // Batch mode only supports tools where incidentId is the only required argument.
        if (requiredSet.Count != 1 || !requiredSet.Contains(IncidentIdParameterName))
        {
            return false;
        }

        string paramType = incidentIdProperty.TryGetProperty("type", out JsonElement typeEl)
            ? typeEl.GetString() ?? "string"
            : "string";

        try
        {
            arguments[IncidentIdParameterName] = ParseInputValue(incidentIdInput, paramType);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void PrintBatchResultsTable(IList<BatchRunResult> results)
    {
        const int funcWidth = 40;
        const int latencyWidth = 12;
        const int statusWidth = 14;
        const int messageWidth = 50;

        string horizontal = $"+{new string('-', funcWidth + 2)}+{new string('-', latencyWidth + 2)}+{new string('-', statusWidth + 2)}+{new string('-', messageWidth + 2)}+";
        Console.WriteLine("Batch execution results:");
        Console.WriteLine(horizontal);
        Console.WriteLine($"| {PadRight("func name", funcWidth)} | {PadRight("latency", latencyWidth)} | {PadRight("success/failed", statusWidth)} | {PadRight("message", messageWidth)} |");
        Console.WriteLine(horizontal);

        foreach (BatchRunResult result in results)
        {
            string latency = $"{result.LatencyMs} ms";
            string status = result.Success ? "success" : "failed";
            string message = result.Success ? string.Empty : result.Message;
            Console.WriteLine($"| {PadRight(result.FunctionName, funcWidth)} | {PadRight(latency, latencyWidth)} | {PadRight(status, statusWidth)} | {PadRight(message, messageWidth)} |");
        }

        Console.WriteLine(horizontal);
    }

    private static string BuildShortErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unknown error";
        }

        return message.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static async Task ShowSpinnerAsync(string label, CancellationToken cancellationToken)
    {
        char[] frames = new[] { '|', '/', '-', '\\' };
        int index = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write($"\r{label} {frames[index % frames.Length]}");
            index++;
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        int clearWidth = label.Length + 4;
        Console.Write("\r" + new string(' ', clearWidth) + "\r");
    }

    private static string PadRight(string value, int width)
    {
        return value.Length > width ? value.Substring(0, width) : value.PadRight(width);
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

    private static Dictionary<string, object?> PromptArgumentsForTool(ToolDoc tool)
    {
        var arguments = new Dictionary<string, object?>();

        if (tool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.InputSchema.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return arguments;
        }

        var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tool.InputSchema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
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

        foreach (JsonProperty prop in properties.EnumerateObject())
        {
            string paramName = prop.Name;
            bool isRequired = requiredSet.Contains(paramName);
            string type = prop.Value.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() ?? "string" : "string";

            if (!isRequired)
            {
                continue;
            }

            while (true)
            {
                Console.Write($"Enter {paramName} ({type}){(isRequired ? " [required]" : string.Empty)}: ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("Value is required.");
                    continue;
                }

                try
                {
                    arguments[paramName] = ParseInputValue(input, type);
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Invalid value: {ex.Message}");
                }
            }
        }

        return arguments;
    }

    private static object? ParseInputValue(string input, string type)
    {
        return type switch
        {
            "integer" => long.Parse(input, CultureInfo.InvariantCulture),
            "number" => double.Parse(input, CultureInfo.InvariantCulture),
            "boolean" => bool.Parse(input),
            "array" => JsonDocument.Parse(input).RootElement.Clone(),
            "object" => JsonDocument.Parse(input).RootElement.Clone(),
            _ => input
        };
    }

    private sealed record ToolDoc(
        int Number,
        string Name,
        string Description,
        string Parameters,
        string ExampleArguments,
        JsonElement InputSchema);

    private sealed record BatchRunResult(string FunctionName, long LatencyMs, bool Success, string Message);
}
