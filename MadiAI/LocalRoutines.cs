using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MadiAI;

/// <summary>
/// Local, explicitly saved desktop routines. No scripts, trading,
/// file deletion, shell commands, or arbitrary user-provided executables.
/// </summary>
internal sealed class LocalRoutines
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "open google", "open youtube", "open notepad", "open calculator",
        "open downloads", "open documents", "open pictures", "open explorer",
        "open settings", "open mt5", "system status", "open canva",
        "open whatsapp", "open ebay", "open aliexpress"
    };

    private readonly string path;
    private readonly Dictionary<string, string[]> routines = new(StringComparer.OrdinalIgnoreCase);

    public LocalRoutines(string profile)
    {
        path = Path.Combine(profile, "routines.json");
        try
        {
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path));
                if (saved != null)
                    foreach (var pair in saved)
                        if (IsValidName(pair.Key) && pair.Value is { Length: > 0 and <= 8 } &&
                            pair.Value.All(x => Allowed.Contains(x)))
                            routines[pair.Key] = pair.Value;
            }
        }
        catch (Exception) { /* Ignore corrupt local settings; never execute invalid routines. */ }
    }

    public IEnumerable<string> Names => routines.Keys.OrderBy(x => x);

    public string Save(string name, string commandList)
    {
        name = name.Trim();
        if (!IsValidName(name)) return "Routine names must be 2–30 letters, numbers, spaces, _ or -.";
        var commands = commandList.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (commands.Length is < 1 or > 8) return "Use 1–8 commands separated by semicolons.";
        if (commands.Any(x => !Allowed.Contains(x)))
            return "Only supported safe commands can be saved: " + string.Join(", ", Allowed.OrderBy(x => x));
        if (routines.Count >= 30 && !routines.ContainsKey(name))
            return "You can save up to 30 local routines.";
        routines[name] = commands;
        SaveFile();
        return "Saved local routine '" + name + "' with " + commands.Length + " steps.";
    }

    public string[]? Get(string name) => routines.TryGetValue(name.Trim(), out var commands) ? commands.ToArray() : null;

    public bool Delete(string name)
    {
        if (!routines.Remove(name.Trim())) return false;
        SaveFile();
        return true;
    }

    private static bool IsValidName(string name) =>
        Regex.IsMatch(name, @"^[a-zA-Z0-9 _-]{2,30}$");

    private void SaveFile()
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(routines, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
