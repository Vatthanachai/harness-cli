using System.Runtime.CompilerServices;

using Aiyara.Harness.Tools.Providers;

using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

namespace Aiyara.Harness.Tools.Tests;

public sealed class DispatchAgentToolTests
{
    private static readonly string[] AllToolNames =
        ["open_file", "list_files", "write_file", "run_command", "handoff", "write_tasks", "update_task", "dispatch_agent"];

    [Fact]
    public void Explore_gets_read_only_tools_and_excludes_write_and_shared_state_tools()
    {
        var sut = MakeSut(out _);

        var names = sut.ToolsFor("explore").Select(NameOf).ToList();

        Assert.Contains("open_file", names);
        Assert.Contains("list_files", names);
        Assert.DoesNotContain("write_file", names);
        Assert.DoesNotContain("run_command", names);
        Assert.DoesNotContain("handoff", names);
        Assert.DoesNotContain("write_tasks", names);
        Assert.DoesNotContain("update_task", names);
        Assert.DoesNotContain("dispatch_agent", names);
    }

    [Fact]
    public void General_gets_write_tools_but_still_excludes_shared_state_tools()
    {
        var sut = MakeSut(out _);

        var names = sut.ToolsFor("general").Select(NameOf).ToList();

        Assert.Contains("open_file", names);
        Assert.Contains("write_file", names);
        Assert.Contains("run_command", names);
        Assert.DoesNotContain("handoff", names);
        Assert.DoesNotContain("write_tasks", names);
        Assert.DoesNotContain("update_task", names);
        Assert.DoesNotContain("dispatch_agent", names);
    }

    [Fact]
    public void Unknown_agent_type_returns_an_error_instead_of_throwing()
    {
        var sut = MakeSut(out _);

        var result = sut.InvokeMethod(new Dictionary<string, object?> { ["agent_type"] = "nope", ["prompt"] = "do it" });

        Assert.Contains("unknown agent_type", (string)result!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_prompt_returns_an_error_instead_of_throwing()
    {
        var sut = MakeSut(out _);

        var result = sut.InvokeMethod(new Dictionary<string, object?> { ["agent_type"] = "explore" });

        Assert.Contains("prompt", (string)result!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_returns_the_sub_agents_accumulated_final_text()
    {
        var sut = MakeSut(out var engine, "Hello", ", ", "world");

        var result = sut.InvokeMethod(new Dictionary<string, object?> { ["agent_type"] = "explore", ["prompt"] = "investigate" });

        Assert.Equal("Hello, world", result);
        Assert.Equal("investigate", engine.LastMessage);
    }

    [Fact]
    public void Sub_agent_uses_the_primary_engines_current_model()
    {
        var sut = MakeSut(out var engine, "ok");
        engine.Model = "some-model:latest";

        sut.InvokeMethod(new Dictionary<string, object?> { ["agent_type"] = "general", ["prompt"] = "task" });

        Assert.Equal("some-model:latest", engine.LastRequestedModel);
    }

    private static DispatchAgentTool MakeSut(out FakeChatEngine engine, params string[] tokens)
    {
        engine = new FakeChatEngine(tokens.Length == 0 ? ["ok"] : tokens);
        var factory = new FakeChatEngineFactory(engine);
        var tools = AllToolNames.Select(n => (object)new Tool { Type = "function", Function = new Function { Name = n } }).ToList();
        return new DispatchAgentTool(factory, "base system prompt", engine, tools);
    }

    private static string NameOf(object tool) => ((Tool)tool).Function!.Name!;

    private sealed class FakeChatEngineFactory(FakeChatEngine engine) : IChatEngineFactory
    {
        public IChatEngine Create(string model, string systemPrompt)
        {
            engine.LastRequestedModel = model;
            return engine;
        }
    }

    private sealed class FakeChatEngine(IEnumerable<string> tokens) : IChatEngine
    {
        private readonly List<string> _tokens = tokens.ToList();

        public string? LastMessage { get; private set; }
        public string? LastRequestedModel { get; set; }

        public List<Message> Messages { get; set; } = [];
        public string Model { get; set; } = "test-model";

        public event EventHandler<string>? OnThink;
        public event EventHandler<Message.ToolCall>? OnToolCall;
        public event EventHandler<ToolResult>? OnToolResult;

        public async IAsyncEnumerable<string> SendAsync(
            string message, IEnumerable<object> tools, [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastMessage = message;
            foreach (var token in _tokens)
            {
                await Task.Yield();
                yield return token;
            }
        }
    }
}
