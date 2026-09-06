using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Chat;
using OpenAI;

namespace CodingSahayi;

public class AgentContextManager
{
    private ChatClient _cloudApiClient = null!;
    private ChatClient _localApiClient = null!;
    private readonly ChatCompletionOptions _chatOptions;
    private readonly List<ChatMessage> _apiHistory = new();
    
    private const int MaxContextMessages = 20;

    public string WorkspaceDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public delegate void ToolStartHandler(string toolCallId, string toolName, string arguments);
    public delegate void ToolEndHandler(string toolCallId, string output, bool success);

    private readonly string _systemPrompt;

    public AgentContextManager(string systemPrompt = null, IEnumerable<string> allowedTools = null)
    {
        _systemPrompt = systemPrompt ?? SettingsManager.SystemPrompt;
        _chatOptions = new ChatCompletionOptions
        {
            AllowParallelToolCalls = false
        };

        var allTools = new List<ChatTool>
        {
            ChatTool.CreateFunctionTool(
                "read_file",
                "Reads a file from the disk.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"}},\"required\":[\"filePath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "write_file",
                "Writes content to a file on the disk.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"content\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "patch_file",
                "Replaces a specific snippet of text in a file with a new snippet.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"targetSnippet\":{\"type\":\"string\"},\"replacementSnippet\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"targetSnippet\",\"replacementSnippet\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "list_directory",
                "Lists files and folders in a directory.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"directoryPath\":{\"type\":\"string\"},\"maxDepth\":{\"type\":\"integer\"}},\"required\":[\"directoryPath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "search_code",
                "Searches for a string across files in a directory.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"searchQuery\":{\"type\":\"string\"},\"fileExtensionFilter\":{\"type\":\"string\"}},\"required\":[\"searchQuery\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "execute_terminal",
                "Executes a command. You do not need to provide a working directory; it will automatically default to the user's selected workspace root.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\"},\"workingDirectory\":{\"type\":\"string\"},\"timeoutSeconds\":{\"type\":\"integer\"}},\"required\":[\"command\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "search_directory",
                "Recursively searches a directory for files matching a pattern.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"searchPattern\":{\"type\":\"string\"}},\"required\":[\"path\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "batch_patch_file",
                "Applies multiple patches to one or more files in a single call. Use this to fix ALL errors at once instead of patching one at a time. Each patch object has filePath, targetSnippet, and replacementSnippet.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"patches\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"targetSnippet\":{\"type\":\"string\"},\"replacementSnippet\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"targetSnippet\",\"replacementSnippet\"]}}},\"required\":[\"patches\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "analyze_structure",
                "Analyzes structural components (classes, functions, interfaces) of a specified source file based on its file extension.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"}},\"required\":[\"filePath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "verify_syntax",
                "Parses a source file based on its file extension and returns syntax errors with line numbers.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"}},\"required\":[\"filePath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "resolve_symbol",
                "Looks up the definition details of a specific symbol in a source file based on its file extension.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"symbolName\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"symbolName\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "semantic_code_search",
                "Performs a semantic vector search across the code base using Ollama embeddings to find relevant code chunks.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "run_tests",
                "Runs tests in a specified directory using dotnet test in a PTY, returning formatted failure output or success.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"projectPath\":{\"type\":\"string\"}},\"required\":[\"projectPath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "train_local_model",
                "Fine-tunes the local model on accumulated ProjectKnowledge entries using Soup CLI (D:\\Soup) with LoRA and layer streaming.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{}}")
            )
        };

        if (allowedTools != null)
        {
            var allowedSet = new HashSet<string>(allowedTools);
            foreach (var tool in allTools)
            {
                if (allowedSet.Contains(tool.FunctionName))
                    _chatOptions.Tools.Add(tool);
            }
        }
        else
        {
            foreach (var tool in allTools)
                _chatOptions.Tools.Add(tool);
        }

        InitializeClient();
        _apiHistory.Add(new SystemChatMessage(_systemPrompt));
    }

    public void ReinitializeClient()
    {
        InitializeClient();
        if (_apiHistory.Count > 0)
        {
            _apiHistory[0] = new SystemChatMessage(_systemPrompt);
        }
        else
        {
            _apiHistory.Add(new SystemChatMessage(_systemPrompt));
        }
    }

    public void LoadHistory(IEnumerable<CodingSahayi.Data.ChatMessageEntity> dbMessages)
    {
        InitializeClient();
        _apiHistory.Clear();
        _apiHistory.Add(new SystemChatMessage(_systemPrompt));
        
        foreach (var msg in dbMessages.OrderBy(m => m.Timestamp))
        {
            if (msg.Role == "User")
            {
                _apiHistory.Add(new UserChatMessage(msg.Content));
            }
            else if (msg.Role == "Agent" || msg.Role == "Assistant")
            {
                _apiHistory.Add(new AssistantChatMessage(msg.Content));
            }
        }
    }

    private void InitializeClient()
    {
        var apiKey = SettingsManager.SecureApiKey;
        var endpoint = SettingsManager.ApiEndpoint;
        if (!endpoint.EndsWith("/")) endpoint += "/";
        
        var model = SettingsManager.ModelName;
        if (model == "Hybrid Router (Auto)" || model == "Local Model Only")
        {
            model = "anthropic/claude-3.5-sonnet-20240620"; // Fallback for the cloud client
        }
        
        var cloudOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        cloudOptions.NetworkTimeout = TimeSpan.FromMinutes(10);
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), cloudOptions);
        _cloudApiClient = client.GetChatClient(model);
        
        var localApiKey = SettingsManager.LocalApiKey;
        var localEndpoint = SettingsManager.LocalApiBaseUrl;
        if (!localEndpoint.EndsWith("/")) localEndpoint += "/";
        
        var localModel = SettingsManager.LocalModelName;
        
        var localOptions = new OpenAIClientOptions { Endpoint = new Uri(localEndpoint) };
        localOptions.NetworkTimeout = TimeSpan.FromMinutes(10);
        var localClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(localApiKey), localOptions);
        _localApiClient = localClient.GetChatClient(localModel);
    }

    public async Task<bool> IsLocalAvailableAsync()
    {
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            var pingHistory = new List<ChatMessage> { new UserChatMessage("Respond with exactly one word: pong.") };
            await _localApiClient.CompleteChatAsync(pingHistory, cancellationToken: cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(string route, string plan)> PlanAndRouteTask(string userPrompt, System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            using var routerCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            routerCts.CancelAfter(TimeSpan.FromSeconds(15));
            
            var routerSystem = new SystemChatMessage(
                "You are a task complexity classifier. Analyze the user's task and respond with ONLY a JSON object.\n" +
                "Evaluate based on: Does it need multi-file edits? Does it need architectural reasoning? Does it need tool calls (reading/writing files, running commands)?\n" +
                "Simple tasks: greetings, Q&A, explanations, single-concept questions, formatting.\n" +
                "Complex tasks: code generation, debugging, refactoring, multi-step builds, file operations.\n" +
                "Output format: {\"Plan\": \"brief 1-sentence plan\", \"ComplexityScore\": <1-10>}");
            var routerUser = new UserChatMessage(userPrompt);
            var routerHistory = new List<ChatMessage> { routerSystem, routerUser };
            
            var routerOptions = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };
            
            var completion = await _localApiClient.CompleteChatAsync(routerHistory, routerOptions, routerCts.Token);
            var responseText = completion.Value.Content[0].Text;
            
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            
            string plan = "";
            if (root.TryGetProperty("Plan", out var planProp) || root.TryGetProperty("plan", out planProp))
                plan = planProp.GetString() ?? "";
            
            int score = 5; // default to cloud
            JsonElement scoreProp;
            if (root.TryGetProperty("ComplexityScore", out scoreProp) || root.TryGetProperty("complexity_score", out scoreProp))
            {
                if (scoreProp.ValueKind == JsonValueKind.Number)
                    score = scoreProp.GetInt32();
                else if (scoreProp.ValueKind == JsonValueKind.String && int.TryParse(scoreProp.GetString(), out int parsed))
                    score = parsed;
            }
            
            string route = score >= 5 ? "API" : "LOCAL";
            return (route, plan);
        }
        catch
        {
            return ("API", "");
        }
    }

    public async Task<string> ProcessMessageAsync(
        string userMessage, 
        Action<string> onStatusUpdate, 
        ToolStartHandler onToolStart,
        ToolEndHandler onToolEnd,
        int maxIterations = 30,
        System.Threading.CancellationToken cancellationToken = default)
    {
        Serilog.Log.Information("ProcessMessageAsync started. User message length: {Length}", userMessage.Length);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        NativeTools._fileBackups.Clear();
        _apiHistory.Add(new UserChatMessage(userMessage));
        PruneContextIfNecessary();

        // --- ROUTING PHASE ---
        string routeDecision = "API";
        ChatClient activeClient = _cloudApiClient;
        string currentModel = SettingsManager.ModelName;
        
        if (currentModel == "Local Model Only")
        {
            routeDecision = "LOCAL";
            activeClient = _localApiClient;
            onStatusUpdate("Using LOCAL model...");
        }
        else if (currentModel == "Hybrid Router (Auto)")
        {
            onStatusUpdate("Checking local model availability...");
            bool localAvailable = await IsLocalAvailableAsync();
            
            if (localAvailable)
            {
                onStatusUpdate("Routing task...");
                var (route, plan) = await PlanAndRouteTask(userMessage, cancellationToken);
                routeDecision = route;
                
                if (routeDecision == "LOCAL")
                {
                    activeClient = _localApiClient;
                    onStatusUpdate(!string.IsNullOrEmpty(plan) 
                        ? $"Routing via LOCAL model — {plan}" 
                        : "Routing via LOCAL model...");
                    
                    // --- MEMORY BANK INJECTION ---
                    try {
                        using var db = new CodingSahayi.Data.AppDbContext();
                        var activeProject = db.Projects.FirstOrDefault(p => p.WorkspacePath == WorkspaceDirectory);
                        if (activeProject != null) {
                            var pastLessons = db.ProjectKnowledgeBase.Where(k => k.ProjectId == activeProject.Id).Select(k => k.LearnedImplementation).ToList();
                            if (pastLessons.Any()) {
                                string appendedLessons = "\n\nProject Context & Past Lessons:\n" + string.Join("\n", pastLessons);
                                if (_apiHistory.FirstOrDefault() is SystemChatMessage sysMsg) {
                                    // Make sure we don't append it multiple times if it's already there
                                    if (!sysMsg.Content[0].Text.Contains("Project Context & Past Lessons")) {
                                        _apiHistory[0] = new SystemChatMessage(sysMsg.Content[0].Text + appendedLessons);
                                    }
                                }
                            }
                        }
                    } catch { }
                }
                else
                {
                    onStatusUpdate(!string.IsNullOrEmpty(plan) 
                        ? $"Routing via CLOUD API — {plan}" 
                        : "Routing via CLOUD API...");
                }
            }
            else
            {
                onStatusUpdate("Local model offline, using CLOUD API...");
            }
        }
        else
        {
            onStatusUpdate("Using CLOUD API...");
            activeClient = _cloudApiClient;
            routeDecision = "API";
        }

        // --- AGENTIC LOOP ---
        int iterationCount = 0;
        bool requiresAction = true;
        string finalResponse = string.Empty;

        while (requiresAction)
        {
            if (iterationCount >= maxIterations)
            {
                finalResponse = "I've hit my iteration limit. How would you like me to proceed?";
                break;
            }
            
            iterationCount++;
            requiresAction = false;
            
            try
            {
                // Retry loop with exponential backoff for rate-limit errors (429/529)
                ChatCompletion completion = null!;
                int maxRetries = 3;
                for (int retry = 0; retry <= maxRetries; retry++)
                {
                    try
                    {
                        onStatusUpdate(retry > 0 
                            ? $"Retrying API call (attempt {retry + 1}/{maxRetries + 1})..." 
                            : $"Calling {(activeClient == _localApiClient ? "LOCAL" : "CLOUD")} model... (Iteration {iterationCount}/{maxIterations})");
                        completion = await activeClient.CompleteChatAsync(_apiHistory, _chatOptions, cancellationToken);
                        break; // Success — exit retry loop
                    }
                    catch (Exception retryEx) when (retry < maxRetries && IsRateLimitError(retryEx))
                    {
                        int delaySeconds = (int)Math.Pow(2, retry + 1); // 2s, 4s, 8s
                        onStatusUpdate($"Rate limited. Retrying in {delaySeconds}s... (attempt {retry + 1}/{maxRetries})");
                        await Task.Delay(delaySeconds * 1000, cancellationToken);
                    }
                    catch (Exception) when (activeClient == _localApiClient && retry == 0)
                    {
                        // Local model failed mid-conversation — fall back to cloud
                        onStatusUpdate("Local model error, falling back to CLOUD API...");
                        activeClient = _cloudApiClient;
                        // retry immediately with cloud
                    }
                }

                if (completion.FinishReason == ChatFinishReason.ToolCalls)
                {
                    _apiHistory.Add(new AssistantChatMessage(completion));
                    onStatusUpdate($"Running {completion.ToolCalls.Count} tool(s)... (Iteration {iterationCount}/{maxIterations})");

                    foreach (var toolCall in completion.ToolCalls)
                    {
                        string toolResult = string.Empty;
                        bool success = true;
                        try
                        {
                            var argsStr = toolCall.FunctionArguments.ToString();
                            onToolStart?.Invoke(toolCall.Id, toolCall.FunctionName, argsStr);

                            int retryCount = 0;
                            bool toolIsModifying = toolCall.FunctionName == "patch_file" || toolCall.FunctionName == "write_file" || toolCall.FunctionName == "batch_patch_file";
                            
                            while (retryCount < 2)
                            {
                                var args = JsonSerializer.Deserialize<JsonElement>(argsStr);
                                var execution = await ExecuteToolAsync(toolCall.FunctionName, args, cancellationToken);
                                toolResult = execution.result;
                                success = execution.success;
                                
                                if (toolIsModifying && success)
                                {
                                    string filePath = ResolvePath(args.TryGetProperty("filePath", out var fp) ? (fp.GetString() ?? "") : "");
                                    if (toolCall.FunctionName == "batch_patch_file") {
                                        if (args.TryGetProperty("patches", out var pArr) && pArr.ValueKind == JsonValueKind.Array && pArr.GetArrayLength() > 0)
                                            filePath = ResolvePath(pArr[0].GetProperty("filePath").GetString() ?? "");
                                    }
                                    
                                    if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath) && filePath.EndsWith(".cs"))
                                    {
                                        bool patchValid = await EvaluatePatchAsync(filePath, argsStr);
                                        if (!patchValid)
                                        {
                                            retryCount++;
                                            if (retryCount >= 2)
                                            {
                                                NativeTools.RollbackChanges();
                                                toolResult = $"CRITICAL FAILURE: Patch was invalid or caused syntax errors. Rolled back all changes.";
                                                success = false;
                                                finalResponse = toolResult;
                                                break;
                                            }
                                            else
                                            {
                                                string syntaxStatus = WorkspaceCodeAnalysisService.VerifySyntax(filePath);
                                                NativeTools.RollbackChanges();
                                                var retryHistory = new List<ChatMessage>(_apiHistory);
                                                retryHistory.Add(new ToolChatMessage(toolCall.Id, $"Error: The patch caused syntax errors:\n{syntaxStatus}\nPlease generate a corrected tool call."));
                                                var retryResp = await activeClient.CompleteChatAsync(retryHistory, _chatOptions, cancellationToken);
                                                if (retryResp.Value.FinishReason == ChatFinishReason.ToolCalls && retryResp.Value.ToolCalls.Count > 0)
                                                {
                                                    argsStr = retryResp.Value.ToolCalls[0].FunctionArguments.ToString();
                                                    continue;
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                // --- MEMORY BANK EXTRACTION ---
                                if (routeDecision == "API" && toolCall.FunctionName == "execute_terminal" && argsStr.Contains("dotnet build") && success && toolResult.Contains("Build succeeded"))
                                {
                                    _ = Task.Run(async () => {
                                        try {
                                            var summaryPrompt = "Summarize the architectural change you just made in 1-2 sentences. Focus on file structure and logic.";
                                            var bgHistory = new List<ChatMessage>(_apiHistory) { new UserChatMessage(summaryPrompt) };
                                            var resp = await _cloudApiClient.CompleteChatAsync(bgHistory, new ChatCompletionOptions { AllowParallelToolCalls = false });
                                            var summaryResponse = resp.Value.Content[0].Text;
                                            
                                            using var bgDb = new CodingSahayi.Data.AppDbContext();
                                            var proj = bgDb.Projects.FirstOrDefault(p => p.WorkspacePath == WorkspaceDirectory);
                                            if (proj != null) {
                                                bgDb.ProjectKnowledgeBase.Add(new CodingSahayi.Data.ProjectKnowledge {
                                                    ProjectId = proj.Id,
                                                    TaskDescription = userMessage,
                                                    LearnedImplementation = summaryResponse,
                                                    DateLearned = DateTime.UtcNow
                                                });
                                                bgDb.SaveChanges();
                                            }
                                        } catch { }
                                    });
                                }
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            toolResult = $"Error parsing tool args: {ex.Message}";
                            success = false;
                        }

                        onToolEnd?.Invoke(toolCall.Id, toolResult, success);
                        _apiHistory.Add(new ToolChatMessage(toolCall.Id, toolResult));
                    }
                    
                    PruneContextIfNecessary();
                    requiresAction = true; 
                }
                else
                {
                    _apiHistory.Add(new AssistantChatMessage(completion));
                    finalResponse = completion.Content[0].Text;
                    
                    if (finalResponse.TrimStart().StartsWith("{") && finalResponse.Contains("\"name\"") && finalResponse.Contains("\"parameters\""))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(finalResponse);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("name", out var nameProp) && root.TryGetProperty("parameters", out var paramsProp))
                            {
                                string toolName = nameProp.GetString() ?? "";
                                string argsStr = paramsProp.ToString();
                                string toolId = "fallback_" + Guid.NewGuid().ToString().Substring(0, 8);
                                
                                onToolStart?.Invoke(toolId, toolName, argsStr);
                                var execution = await ExecuteToolAsync(toolName, paramsProp, cancellationToken);
                                onToolEnd?.Invoke(toolId, execution.result, execution.success);
                                
                                _apiHistory.Add(new UserChatMessage($"[Tool Execution Result]:\n{execution.result}"));
                                requiresAction = true;
                                continue;
                            }
                        }
                        catch { }
                    }

                    PruneContextIfNecessary();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error in agent loop");
                finalResponse = $"**Error:** {ex.Message}";
                requiresAction = false;
            }
        }
        
        if (NativeTools._fileBackups.Count > 0 && !finalResponse.Contains("**Error:**") && !finalResponse.Contains("hit my iteration limit"))
        {
            var diffs = string.Join("\n\n", NativeTools._fileBackups.Keys.Select(k => $"File: {k}\n(Modified)"));
            _ = Task.Run(async () => {
                try {
                    var summaryPrompt = $"Generate a concise 'Design Pattern/Implementation Lesson' summarizing these changes based on the user prompt: '{userMessage}'. Modified files:\n{diffs}";
                    var bgHistory = new List<ChatMessage>(_apiHistory) { new UserChatMessage(summaryPrompt) };
                    var resp = await _localApiClient.CompleteChatAsync(bgHistory, new ChatCompletionOptions { AllowParallelToolCalls = false });
                    var summaryResponse = resp.Value.Content[0].Text;
                    
                    using var bgDb = new CodingSahayi.Data.AppDbContext();
                    var proj = bgDb.Projects.FirstOrDefault(p => p.WorkspacePath == WorkspaceDirectory);
                    if (proj != null) {
                        bgDb.ProjectKnowledgeBase.Add(new CodingSahayi.Data.ProjectKnowledge {
                            ProjectId = proj.Id,
                            TaskDescription = userMessage,
                            LearnedImplementation = summaryResponse,
                            DateLearned = DateTime.UtcNow
                        });
                        bgDb.SaveChanges();
                    }
                } catch { }
            });
        }
        
        stopwatch.Stop();
        Serilog.Log.Information("ProcessMessageAsync completed. Final response length: {Length}. Routing: {Route}. Elapsed: {ElapsedMs}ms", finalResponse.Length, routeDecision, stopwatch.ElapsedMilliseconds);
        return finalResponse;
    }
    
    private async Task<bool> EvaluatePatchAsync(string filePath, string proposedPatch)
    {
        string syntaxStatus = WorkspaceCodeAnalysisService.VerifySyntax(filePath);
        if (syntaxStatus == "Syntax OK" || syntaxStatus.Contains("Syntax OK - Fallback")) return true;

        var systemPrompt = new SystemChatMessage("You are a code critic. The following file has syntax errors after a patch. Analyze the errors and the proposed patch. Reply with ONLY 'REJECT' if the patch is broken, or 'ACCEPT' if the error is a false positive.");
        var userPrompt = new UserChatMessage($"File: {filePath}\nErrors:\n{syntaxStatus}\nPatch:\n{proposedPatch}");
        
        try
        {
            var resp = await _localApiClient.CompleteChatAsync(new List<ChatMessage> { systemPrompt, userPrompt });
            if (resp.Value.Content[0].Text.Contains("REJECT")) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(string result, bool success)> ExecuteToolAsync(string toolName, JsonElement args, System.Threading.CancellationToken cancellationToken = default)
    {
        string toolResult = string.Empty;
        bool success = true;
        try
        {
            switch (toolName)
            {
                case "read_file":
                    toolResult = NativeTools.ReadFile(ResolvePath(args.GetProperty("filePath").GetString() ?? ""));
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "write_file":
                case "patch_file":
                    string wFilePath = ResolvePath(args.GetProperty("filePath").GetString() ?? "");
                    string newText = "";
                    if (toolName == "write_file")
                    {
                        newText = NativeTools.WriteFile(wFilePath, args.GetProperty("content").GetString() ?? "");
                    }
                    else
                    {
                        newText = NativeTools.PatchFile(
                            wFilePath, 
                            args.GetProperty("targetSnippet").GetString() ?? "", 
                            args.GetProperty("replacementSnippet").GetString() ?? "");
                        if (newText.StartsWith("Error"))
                        {
                            toolResult = newText;
                            success = false;
                            break;
                        }
                    }

                    string oldText = System.IO.File.Exists(wFilePath) ? System.IO.File.ReadAllText(wFilePath) : "";
                    
                    var diff = DiffManager.GenerateDiff(oldText, newText);
                    var tcs = new TaskCompletionSource<bool>();
                    
                    var dispatcher = (Microsoft.UI.Xaml.Application.Current as App)?._window?.DispatcherQueue;
                    if (dispatcher != null)
                    {
                        dispatcher.TryEnqueue(async () =>
                        {
                            var dialog = new DiffReviewDialog(diff);
                            dialog.XamlRoot = (Microsoft.UI.Xaml.Application.Current as App)?._window?.Content.XamlRoot;
                            await dialog.ShowAsync();
                            tcs.SetResult(dialog.IsAccepted);
                        });
                        
                        bool accepted = await tcs.Task;
                        if (accepted)
                        {
                            if (!NativeTools._fileBackups.ContainsKey(wFilePath))
                                NativeTools._fileBackups[wFilePath] = oldText;

                            var dir = System.IO.Path.GetDirectoryName(wFilePath);
                            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                            System.IO.File.WriteAllText(wFilePath, newText);
                            toolResult = $"Success: Applied changes to {wFilePath}";
                        }
                        else
                        {
                            toolResult = $"Error: User rejected the changes to {wFilePath}.";
                            success = false;
                        }
                    }
                    else
                    {
                        // Fallback if UI is not available
                        System.IO.File.WriteAllText(wFilePath, newText);
                        toolResult = $"Success: Applied changes to {wFilePath} (Auto-accepted)";
                    }
                    break;
                case "list_directory":
                    string dirPath = args.TryGetProperty("directoryPath", out var p) ? p.GetString() ?? WorkspaceDirectory : WorkspaceDirectory;
                    toolResult = NativeTools.ListDirectory(ResolvePath(dirPath), GetIntProperty(args, "maxDepth", 3));
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "search_code":
                    toolResult = NativeTools.SearchCode(
                        WorkspaceDirectory,
                        args.GetProperty("searchQuery").GetString() ?? "",
                        args.TryGetProperty("fileExtensionFilter", out var fe) ? (fe.GetString() ?? "*.*") : "*.*");
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "search_directory":
                    string sPath = args.TryGetProperty("path", out var sp) ? (sp.GetString() ?? "") : "";
                    string sPattern = args.TryGetProperty("searchPattern", out var spt) ? (spt.GetString() ?? "*") : "*";
                    toolResult = NativeTools.SearchDirectory(ResolvePath(sPath), sPattern);
                    if (toolResult.StartsWith("Error") || toolResult.StartsWith("Access denied") || toolResult.StartsWith("Directory not found")) success = false;
                    break;
                case "execute_terminal":
                    string wdPath = args.TryGetProperty("workingDirectory", out var wd) ? (wd.GetString() ?? "") : "";
                    string resolvedWd = ResolvePath(wdPath);
                    if (System.IO.File.Exists(resolvedWd)) resolvedWd = System.IO.Path.GetDirectoryName(resolvedWd) ?? "";

                    toolResult = NativeTools.ExecuteTerminalSafe(
                        args.GetProperty("command").GetString() ?? "",
                        resolvedWd,
                        GetIntProperty(args, "timeoutSeconds", 45),
                        cancellationToken);
                    if (toolResult.StartsWith("Failed") || toolResult.Contains("TIMED OUT") || toolResult.StartsWith("Cancelled")) success = false;
                    break;
                case "analyze_structure":
                    toolResult = WorkspaceCodeAnalysisService.AnalyzeStructure(ResolvePath(args.GetProperty("filePath").GetString() ?? ""));
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "verify_syntax":
                    toolResult = WorkspaceCodeAnalysisService.VerifySyntax(ResolvePath(args.GetProperty("filePath").GetString() ?? ""));
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "resolve_symbol":
                    toolResult = WorkspaceCodeAnalysisService.ResolveSymbol(
                        ResolvePath(args.GetProperty("filePath").GetString() ?? ""),
                        args.GetProperty("symbolName").GetString() ?? "");
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "semantic_code_search":
                    toolResult = NativeTools.SemanticCodeSearch(args.GetProperty("query").GetString() ?? "");
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "run_tests":
                    toolResult = await TestRunnerTool.RunTestsAsync(ResolvePath(args.GetProperty("projectPath").GetString() ?? ""));
                    if (toolResult.Contains("Test Failures Detected") || toolResult.StartsWith("Error")) success = false;
                    break;
                case "train_local_model":
                    string targetWorkspace = !string.IsNullOrWhiteSpace(WorkspaceDirectory) ? WorkspaceDirectory :
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodingSahayi", "FineTuning");
                    var trainRes = await ModelTrainerTool.RunSoupTrainingAsync(targetWorkspace);
                    toolResult = trainRes.ExitMessage;
                    if (trainRes.Metrics.Count > 0)
                    {
                        toolResult += $"\nMetrics: {trainRes.Metrics.Count} loss checkpoints recorded. Final loss: {trainRes.Metrics[^1].Loss:F4}";
                    }
                    if (toolResult.Contains("failed", StringComparison.OrdinalIgnoreCase) || toolResult.StartsWith("❌"))
                        success = false;
                    break;
                default:
                    if (toolName == "batch_patch_file")
                    {
                        var sb = new System.Text.StringBuilder();
                        int successCount = 0, failCount = 0;
                        if (args.TryGetProperty("patches", out var patches) && patches.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var patch in patches.EnumerateArray())
                            {
                                string pFile = ResolvePath(patch.GetProperty("filePath").GetString() ?? "");
                                string pTarget = patch.GetProperty("targetSnippet").GetString() ?? "";
                                string pReplace = patch.GetProperty("replacementSnippet").GetString() ?? "";
                                string pResult = NativeTools.PatchFile(pFile, pTarget, pReplace);
                                sb.AppendLine($"[{System.IO.Path.GetFileName(pFile)}]: {pResult}");
                                if (pResult.StartsWith("Error")) failCount++; else successCount++;
                            }
                        }
                        toolResult = $"Batch complete: {successCount} succeeded, {failCount} failed.\n{sb}";
                        if (failCount > 0) success = false;
                    }
                    else
                    {
                        toolResult = $"Unknown tool: {toolName}";
                        success = false;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            toolResult = $"Error executing tool: {ex.Message}";
            success = false;
        }
        return (toolResult, success);
    }
    
    private void PruneContextIfNecessary()
    {
        if (_apiHistory.Count <= MaxContextMessages) return;

        var systemMessage = _apiHistory.FirstOrDefault();
        
        while (_apiHistory.Count > MaxContextMessages * 0.8)
        {
            if (_apiHistory.Count > 1)
            {
                _apiHistory.RemoveAt(1);
            }
        }
    }

    private static bool IsRateLimitError(Exception ex)
    {
        var message = ex.Message ?? "";
        // Check for HTTP status codes 429 (Too Many Requests) or 529 (Overloaded)
        return message.Contains("429") || message.Contains("529") 
            || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("overloaded", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return WorkspaceDirectory;
        return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(WorkspaceDirectory, path);
    }

    private int GetIntProperty(JsonElement element, string propertyName, int defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int num)) return num;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int strNum)) return strNum;
        }
        return defaultValue;
    }
}
