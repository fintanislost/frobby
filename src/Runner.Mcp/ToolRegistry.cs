using System.Collections.Generic;
using System.Linq;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>Name-indexed tool lookup. Registered once at server startup; read-only thereafter.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public void Register(ITool tool) => _tools[tool.Name] = tool;

    public ITool? Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;

    public IReadOnlyList<ITool> All() => _tools.Values.ToArray();
}
