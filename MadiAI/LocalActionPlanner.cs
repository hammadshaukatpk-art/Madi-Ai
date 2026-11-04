using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MadiAI;

/// <summary>
/// Local model interprets natural commands, but never receives arbitrary
/// process execution privileges. The UI performs only allowlisted actions.
/// </summary>
internal static class LocalActionPlanner
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "open_google", "open_youtube", "open_downloads", "open_documents",
        "open_desktop", "open_pictures", "open_calculator", "open_notepad",
        "open_settings", "open_explorer", "open_mt5", "mt5_status",
        "search_web", "search_youtube", "open_app", "focus_app", "none"
    };

    internal sealed record ActionPlan(string Action, string Argument);

    public static async Task<ActionPlan?> PlanAsync(HttpClient client, string model, string instruction,
        CancellationToken cancellationToken)
    {
        const string policy = """
            You classify user commands for a Windows desktop assistant.
            Return ONLY a JSON object with keys "action" and "argument".
            Allowed actions: open_google, open_youtube, open_downloads,
            open_documents, open_desktop, open_pictures, open_calculator,
            open_notepad, open_settings, open_explorer, open_mt5, mt5_status,
            search_web, search_youtube, open_app, focus_app, none.
            Use none for ordinary conversation, uncertainty, requests for
            trading, financial operations, purchases, sending messages,
            deleting files, shell commands or unsupported actions.
            For open_app or focus_app put only the application name in argument.
            For search_web and search_youtube put only the search phrase.
            Never generate file paths, command-line arguments, scripts or URLs.
            Do not invent capabilities. Classify English, Urdu, Hindi, Roman Urdu.
            Examples:
            "Could you bring up my downloads?" -> {"action":"open_downloads","argument":""}
            "Google kholo" -> {"action":"open_google","argument":""}
            "Search for recipes for pasta" -> {"action":"search_web","argument":"recipes for pasta"}
            "How are you?" -> {"action":"none","argument":""}
            "Buy 2 lots EURUSD" -> {"action":"none","argument":""}
            """;
        var payload = JsonSerializer.Serialize(new
        {
            model,
            stream = false,
            format = "json",
            messages = new object[]
            {
                new { role = "system", content = policy },
                new { role = "user", content = instruction }
            },
            options = new { temperature = 0, num_predict = 90 }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:11434/api/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var message = data.RootElement.GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(message)) return null;
        using var result = JsonDocument.Parse(message);
        var action = result.RootElement.GetProperty("action").GetString() ?? "none";
        var argument = result.RootElement.TryGetProperty("argument", out var arg) ? arg.GetString() ?? "" : "";
        if (!Allowed.Contains(action) || argument.Length > 150) return null;
        return new ActionPlan(action, argument.Trim());
    }
}
