using System.Net.Http.Headers;
using System.Text;

using Aiyara.Harness.Cli;
using Aiyara.Harness.Cli.Commands;
using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Models.Enums;
using Aiyara.Harness.Tools;
using Aiyara.Harness.Tools.Mcp;
using Aiyara.Harness.Tools.Providers;
using Aiyara.Harness.Tools.Rag;
using Aiyara.Harness.Tools.Providers.LmStudio;
using Aiyara.Harness.Tools.Providers.Ollama;

using Microsoft.Extensions.Configuration;

using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

using Serilog;

Console.OutputEncoding = Encoding.UTF8;
try { Console.InputEncoding = Encoding.UTF8; } catch (IOException) { /* no attached console, e.g. redirected input */ }
if (OperatingSystem.IsWindows())
    NativeConsole.EnableAnsiColors();

if (args.Length > 0 && args[0] == "config")
    return ConfigCommand.Run(args[1..]);

try
{
    Workspace.Initialize(args.Length > 0 ? args[0] : null);
}
catch (DirectoryNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

if (!TrustStore.IsWorkspaceTrusted(Workspace.Root))
{
    var trusted = ConsentPrompt.Confirm(
        "folder",
        [Workspace.Root, "Aiyara Harness and the model it runs will be able to read and write files here on your behalf."],
        "Do you trust the files in this folder?");

    if (!trusted)
    {
        Console.WriteLine("Exiting - folder was not trusted.");
        return 1;
    }

    TrustStore.TrustWorkspace(Workspace.Root);
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

// The file sink's path can't be set from appsettings.json - it needs to resolve to the current
// user's profile at runtime (%USERPROFILE%\.aiyara\logs\), not a path relative to wherever the
// process happens to be launched from, so every workspace's session lands in the same place
// alongside the rest of the user-level config instead of scattering a "logs\" folder into
// whichever directory the harness was run from.
var logFilePath = Path.Combine(UserConfigPaths.Directory, "logs", "harness-.log");

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
    .CreateLogger();

var mcpClients = new List<IAsyncDisposable>();

try
{
    var models = UserConfigStore.Load("models.json", new ModelsOptions());
    var mcpOptions = UserConfigStore.Load("mcp.json", new McpOptions());
    var ragOptions = UserConfigStore.Load("rag.json", new RagOptions());

    var systemPrompt = new StringBuilder(Persona.SystemPrompt);
    systemPrompt.Append($"\n\n{Persona.WorkingPrinciples}");
    systemPrompt.Append($"\n\n{Persona.ToolchainPolicy}");
    systemPrompt.Append($"\n\n{Persona.VerificationPolicy}");
    systemPrompt.Append($"\n\n{Persona.TaskTrackingPolicy}");
    systemPrompt.Append($"\n\n{Persona.SkillPolicy}");

    var skillRegistry = new SkillRegistry();
    var skillCatalog = skillRegistry.Enabled;
    if (skillCatalog.Count > 0)
    {
        systemPrompt.Append("\n\n# Available skills\n\n");
        systemPrompt.Append(string.Join('\n', skillCatalog.Select(s => $"- {s.Name}: {s.Description}")));
    }

    systemPrompt.Append($"\n\n{Persona.ProjectDocsPolicy}");

    foreach (var (fileName, heading, content) in new[]
             {
                 (AiyaraDocument.FileName, "Project overview", AiyaraDocument.Load()),
                 (FilesDocument.FileName, "Project files", FilesDocument.Load()),
                 (ToolsDocument.FileName, "Tool usage conventions", ToolsDocument.Load()),
                 (CommandsDocument.FileName, "Build/test/run commands", CommandsDocument.Load()),
                 (MemoryDocument.FileName, "Persistent notes", MemoryDocument.Load())
             })
    {
        if (content is null) continue;

        Log.Information("Loaded {FileName} from workspace root into the system prompt", fileName);
        systemPrompt.Append($"\n\n# {heading} ({fileName})\n\n{content}");
    }

    IChatEngine chatEngine;
    IModelCatalog modelCatalog;
    IEmbeddingClient embeddingClient;

    // HttpClient's default Timeout is 100 seconds, which a local reasoning model blows through
    // routinely once it starts a long "thinking" phase (e.g. for a planning-style prompt) - the
    // request gets aborted mid-stream and the exception, uncaught, used to take the whole harness
    // down. Local generation has no reason to be bounded like a web API call - applies to both
    // providers below.
    if (models.Provider == Provider.Ollama)
    {
        var ollamaConnection = UserConfigStore.Load("ollama.json", new OllamaConnectionOptions());
        var httpClient = new HttpClient { BaseAddress = new Uri(ollamaConnection.BaseUrl), Timeout = TimeSpan.FromMinutes(10) };
        if (!string.IsNullOrWhiteSpace(ollamaConnection.AccessToken))
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", ollamaConnection.AccessToken);

        var ollama = new OllamaApiClient(httpClient, models.Default);

        // 4096 is nowhere near enough for a "thinking" model: reasoning content for a non-trivial
        // prompt (e.g. "make a plan for X") easily runs past it, forcing Ollama's context to shift
        // mid-thought and derailing the model so it never reaches a final answer. Confirmed by hand:
        // the same planning prompt at num_ctx=4096 was still streaming pure thinking tokens past 3
        // minutes with no end in sight, while at num_ctx=32768 it reached real answer content well
        // within the same window.
        var chat = new Chat(ollama, systemPrompt.ToString())
        {
            Think = ThinkValue.High,
            Options = new RequestOptions { Temperature = 0.7f, TopP = 0.9f, NumCtx = 32768 }
        };

        chatEngine = new OllamaChatEngine(chat);
        modelCatalog = new OllamaModelCatalog(ollama);
        embeddingClient = new OllamaEmbeddingClient(ollama);
    }
    else
    {
        var lmStudioConnection = UserConfigStore.Load("lmstudio.json", new LmStudioConnectionOptions());
        var baseUrl = lmStudioConnection.BaseUrl.TrimEnd('/') + "/";
        var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(10) };
        if (!string.IsNullOrWhiteSpace(lmStudioConnection.AccessToken))
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", lmStudioConnection.AccessToken);

        chatEngine = new LmStudioChatEngine(httpClient, models.Default, systemPrompt.ToString());
        modelCatalog = new LmStudioModelCatalog(httpClient);
        embeddingClient = new LmStudioEmbeddingClient(httpClient);
    }

    var taskBoard = new TaskBoard();

    List<object> tools =
    [
        new DateTimeTool(),
        new OpenFileTools(),
        new OpenImageTool(),
        new ListFilesTool(),
        new WriteFileTool(),
        new SaveImageTool(),
        new SetStatuslineCommandTool(),
        new AiyaraDocumentTool(),
        new FilesDocumentTool(),
        new ToolsDocumentTool(),
        new CommandsDocumentTool(),
        new MemoryDocumentTool(),
        new RunCommandTool(),
        new HandoffTool(chatEngine),
        new WriteTasksTool(taskBoard),
        new UpdateTaskTool(taskBoard),
        new WriteSkillTool(),
        new DeleteSkillTool(),
        new UseSkillTool(skillRegistry),
        new ListSkillsTool(skillRegistry)
    ];

    var mcpResult = await McpToolLoader.LoadAsync(mcpOptions.Servers);
    tools.AddRange(mcpResult.Tools);
    mcpClients.AddRange(mcpResult.Clients);

    if (ragOptions.Enabled)
    {
        try
        {
            IVectorStore vectorStore = ragOptions.Backend == VectorStoreBackend.Sqlite
                ? await SqliteVectorStore.FromOptionsAsync(ragOptions)
                : JsonVectorStore.FromOptions(ragOptions);

            var stats = await RagIndexBuilder.BuildAsync(ragOptions, embeddingClient, vectorStore);
            tools.Add(new SearchDocumentsTool(
                vectorStore, RagIndexBuilder.Collection, embeddingClient, ragOptions.EmbeddingModel, ragOptions.TopK));

            Log.Information("RAG index ready: {Files} file(s), {Chunks} chunk(s)", stats.FileCount, stats.ChunkCount);
        }
        catch (Exception ex)
        {
            // A bad DocumentsPath, an embedding model that isn't actually available, or a corrupt
            // index file shouldn't stop the harness from starting - same resilience policy already
            // applied to a broken MCP server or an invalid config file.
            Log.Warning(ex, "Couldn't build the RAG index - search_documents will not be available this session");
        }
    }

    chatEngine.OnToolCall += (_, call) => Log.Information("Model wants to call: {ToolName}", call.Function?.Name);
    chatEngine.OnToolResult += (_, call) => Log.Information("Tool returned: {ToolResult}", call.Result);

    var toolRegistry = new ToolRegistry(tools);

    var slashCommands = new SlashCommandRegistry(
    [
        ConfigSlashCommand.Create(),
        ModelSlashCommand.Create(),
        ToolsSlashCommand.Create(),
        SkillsSlashCommand.Create(),
        ClearSlashCommand.Create(),
        WorkspaceSlashCommand.Create()
    ]);

    using var terminalUI = new TerminalUI(slashCommands.All.Count);
    terminalUI.Initialize(models.Default, Workspace.Root);

    var slashContext = new SlashCommandContext(modelCatalog, chatEngine, toolRegistry, skillRegistry, terminalUI);
    var chatSession = new ChatSession(chatEngine, toolRegistry, slashCommands, slashContext, terminalUI);

    if (!File.Exists(AiyaraDocument.PathAtWorkspaceRoot))
    {
        var wantsInit = ConsentPrompt.Confirm(
            "AIYARA.md generation",
            [$"No {AiyaraDocument.FileName} found for this project."],
            "Create one now so future sessions start with project context already loaded?");

        if (wantsInit)
        {
            await chatSession.RunInitTurnAsync(
                $"Explore this project (e.g. list_files, and open_file on things like README.md if present), " +
                $"then create {AiyaraDocument.FileName} at the workspace root via write_aiyara_document: a " +
                "concise overview of what the project is, its structure, and any conventions worth knowing " +
                "up front for future sessions.");
        }
    }

    await chatSession.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Harness.Cli terminated unexpectedly");
}
finally
{
    foreach (var client in mcpClients)
        await client.DisposeAsync();

    Log.CloseAndFlush();
}

return 0;