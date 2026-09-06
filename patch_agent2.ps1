$code = Get-Content "d:\Coding Sahayi\AgentContextManager.cs" -Raw

$newCode = $code -replace '(?s)        // --- ROUTING PHASE ---.*?(?=        if \(NativeTools\._fileBackups\.Count > 0)', @"
        // --- ROUTING PHASE ---
        string routeDecision = ""API"";
        CodingSahayi.Data.ModelEndpointConfig activeConfig = null;
        string currentModel = SettingsManager.ModelName;
        
        if (currentModel == ""Local Model Only"")
        {
            routeDecision = ""LOCAL"";
            activeConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
            onStatusUpdate(""Using LOCAL model..."");
        }
        else if (currentModel == ""Hybrid Router (Auto)"")
        {
            onStatusUpdate(""Checking local model availability..."");
            bool localAvailable = await IsLocalAvailableAsync();
            
            if (localAvailable)
            {
                onStatusUpdate(""Routing task..."");
                var (route, plan) = await PlanAndRouteTask(userMessage, cancellationToken);
                routeDecision = route;
                
                if (routeDecision == ""LOCAL"")
                {
                    activeConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
                    onStatusUpdate(!string.IsNullOrEmpty(plan) 
                        ? $""Routing via LOCAL model — {plan}"" 
                        : ""Routing via LOCAL model..."");
                    
                    // --- MEMORY BANK INJECTION ---
                    try {
                        using var db = new CodingSahayi.Data.AppDbContext();
                        var activeProject = db.Projects.FirstOrDefault(p => p.WorkspacePath == WorkspaceDirectory);
                        if (activeProject != null) {
                            var pastLessons = db.ProjectKnowledgeBase.Where(k => k.ProjectId == activeProject.Id).Select(k => k.LearnedImplementation).ToList();
                            if (pastLessons.Any()) {
                                string appendedLessons = ""\n\nProject Context & Past Lessons:\n"" + string.Join(""\n"", pastLessons);
                                if (_apiHistory.FirstOrDefault() is SystemChatMessage sysMsg) {
                                    if (!sysMsg.Content[0].Text.Contains(""Project Context & Past Lessons"")) {
                                        _apiHistory[0] = new SystemChatMessage(sysMsg.Content[0].Text + appendedLessons);
                                    }
                                }
                            }
                        }
                    } catch { }
                }
                else
                {
                    activeConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Cloud && m.IsEnabled);
                    onStatusUpdate(!string.IsNullOrEmpty(plan) 
                        ? $""Routing via CLOUD API — {plan}"" 
                        : ""Routing via CLOUD API..."");
                }
            }
            else
            {
                activeConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Cloud && m.IsEnabled);
                onStatusUpdate(""Local model offline, using CLOUD API..."");
            }
        }
        else
        {
            onStatusUpdate(""Using CLOUD API..."");
            
            // Extract display name from format
            string displayMatch = currentModel;
            int parenIdx = currentModel.IndexOf("" ("");
            if (parenIdx > 0) displayMatch = currentModel.Substring(0, parenIdx);
            
            activeConfig = SettingsManager.ConfiguredModels.FirstOrDefault(m => m.DisplayName == displayMatch) ?? 
                           SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Cloud && m.IsEnabled);
            routeDecision = ""API"";
        }

        if (activeConfig == null)
        {
            activeConfig = SettingsManager.ConfiguredModels.FirstOrDefault();
        }

        // --- AGENTIC LOOP ---
        int iterationCount = 0;
        bool requiresAction = true;
        string finalResponse = string.Empty;

        while (requiresAction)
        {
            if (iterationCount >= maxIterations)
            {
                finalResponse = ""I've hit my iteration limit. How would you like me to proceed?"";
                break;
            }
            
            iterationCount++;
            requiresAction = false;
            
            try
            {
                onStatusUpdate($""Calling model {activeConfig?.DisplayName}... (Iteration {iterationCount}/{maxIterations})"");
                ChatCompletion completion = await ExecuteWithFallbackAsync(activeConfig, _apiHistory, _chatOptions, onStatusUpdate, cancellationToken);

                if (completion.FinishReason == ChatFinishReason.ToolCalls)
                {
                    _apiHistory.Add(new AssistantChatMessage(completion));
                    onStatusUpdate($""Running {completion.ToolCalls.Count} tool(s)... (Iteration {iterationCount}/{maxIterations})"");

                    foreach (var toolCall in completion.ToolCalls)
                    {
                        string toolResult = string.Empty;
                        bool success = true;
                        try
                        {
                            var argsStr = toolCall.FunctionArguments.ToString();
                            onToolStart?.Invoke(toolCall.Id, toolCall.FunctionName, argsStr);

                            int retryCount = 0;
                            bool toolIsModifying = toolCall.FunctionName == ""patch_file"" || toolCall.FunctionName == ""write_file"" || toolCall.FunctionName == ""batch_patch_file"";
                            
                            while (retryCount < 2)
                            {
                                var args = JsonSerializer.Deserialize<JsonElement>(argsStr);
                                var execution = await ExecuteToolAsync(toolCall.FunctionName, args, cancellationToken);
                                toolResult = execution.result;
                                success = execution.success;
                                
                                if (toolIsModifying && success)
                                {
                                    string filePath = ResolvePath(args.TryGetProperty(""filePath"", out var fp) ? (fp.GetString() ?? """") : """");
                                    if (toolCall.FunctionName == ""batch_patch_file"") {
                                        if (args.TryGetProperty(""patches"", out var pArr) && pArr.ValueKind == JsonValueKind.Array && pArr.GetArrayLength() > 0)
                                            filePath = ResolvePath(pArr[0].GetProperty(""filePath"").GetString() ?? """");
                                    }
                                    
                                    if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath) && filePath.EndsWith("".cs""))
                                    {
                                        bool patchValid = await EvaluatePatchAsync(filePath, argsStr);
                                        if (!patchValid)
                                        {
                                            retryCount++;
                                            if (retryCount >= 2)
                                            {
                                                NativeTools.RollbackChanges();
                                                toolResult = $""CRITICAL FAILURE: Patch was invalid or caused syntax errors. Rolled back all changes."";
                                                success = false;
                                                finalResponse = toolResult;
                                                break;
                                            }
                                            else
                                            {
                                                string syntaxStatus = WorkspaceCodeAnalysisService.VerifySyntax(filePath);
                                                NativeTools.RollbackChanges();
                                                var retryHistory = new List<ChatMessage>(_apiHistory);
                                                retryHistory.Add(new ToolChatMessage(toolCall.Id, $""Error: The patch caused syntax errors:\n{syntaxStatus}\nPlease generate a corrected tool call.""));
                                                var clientRetry = GetChatClient(activeConfig);
                                                var retryResp = await clientRetry.CompleteChatAsync(retryHistory, _chatOptions, cancellationToken);
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
                                if (routeDecision == ""API"" && toolCall.FunctionName == ""execute_terminal"" && argsStr.Contains(""dotnet build"") && success && toolResult.Contains(""Build succeeded""))
                                {
                                    _ = Task.Run(async () => {
                                        try {
                                            var summaryPrompt = ""Summarize the architectural change you just made in 1-2 sentences. Focus on file structure and logic."";
                                            var bgHistory = new List<ChatMessage>(_apiHistory) { new UserChatMessage(summaryPrompt) };
                                            var clientBg = GetChatClient(SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Cloud && m.IsEnabled));
                                            var resp = await clientBg.CompleteChatAsync(bgHistory, new ChatCompletionOptions { AllowParallelToolCalls = false });
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
                            toolResult = $""Error parsing tool args: {ex.Message}"";
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
                    
                    if (finalResponse.TrimStart().StartsWith(""{"") && finalResponse.Contains(""\""name\"""") && finalResponse.Contains(""\""parameters\""""))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(finalResponse);
                            var root = doc.RootElement;
                            if (root.TryGetProperty(""name"", out var nameProp) && root.TryGetProperty(""parameters"", out var paramsProp))
                            {
                                string toolName = nameProp.GetString() ?? """";
                                string argsStr = paramsProp.ToString();
                                string toolId = ""fallback_"" + Guid.NewGuid().ToString().Substring(0, 8);
                                
                                onToolStart?.Invoke(toolId, toolName, argsStr);
                                var execution = await ExecuteToolAsync(toolName, paramsProp, cancellationToken);
                                onToolEnd?.Invoke(toolId, execution.result, execution.success);
                                
                                _apiHistory.Add(new UserChatMessage($""[Tool Execution Result]:\n{execution.result}""));
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
                Serilog.Log.Error(ex, ""Error in agent loop"");
                finalResponse = $""**Error:** {ex.Message}"";
                requiresAction = false;
            }
        }
"@

$newCode = $newCode -replace '(?s)    private async Task<bool> EvaluatePatchAsync\([^)]*\)\s*\{.*?return true;\s*\}\s*catch\s*\{.*?return false;\s*\}\s*\}', @"
    private async Task<bool> EvaluatePatchAsync(string filePath, string proposedPatch)
    {
        string syntaxStatus = WorkspaceCodeAnalysisService.VerifySyntax(filePath);
        if (syntaxStatus == ""Syntax OK"" || syntaxStatus.Contains(""Syntax OK - Fallback"")) return true;

        var systemPrompt = new SystemChatMessage(""You are a code critic. The following file has syntax errors after a patch. Analyze the errors and the proposed patch. Reply with ONLY 'REJECT' if the patch is broken, or 'ACCEPT' if the error is a false positive."");
        var userPrompt = new UserChatMessage($""File: {filePath}\nErrors:\n{syntaxStatus}\nPatch:\n{proposedPatch}"");
        
        try
        {
            var localConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
            var client = GetChatClient(localConfig ?? SettingsManager.ConfiguredModels.First());
            var resp = await client.CompleteChatAsync(new List<ChatMessage> { systemPrompt, userPrompt });
            if (resp.Value.Content[0].Text.Contains(""REJECT"")) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ChatCompletion> ExecuteWithFallbackAsync(CodingSahayi.Data.ModelEndpointConfig primaryConfig, List<ChatMessage> history, ChatCompletionOptions options, Action<string> onStatusUpdate, System.Threading.CancellationToken cancellationToken)
    {
        var currentConfig = primaryConfig;
        
        while (currentConfig != null)
        {
            var client = GetChatClient(currentConfig);
            int maxRetries = 3;
            for (int retry = 0; retry <= maxRetries; retry++)
            {
                try
                {
                    return await client.CompleteChatAsync(history, options, cancellationToken);
                }
                catch (Exception ex) when (retry < maxRetries && IsRateLimitError(ex))
                {
                    int delaySeconds = (int)Math.Pow(2, retry + 1);
                    onStatusUpdate($""Rate limited. Retrying in {delaySeconds}s... (attempt {retry + 1}/{maxRetries})"");
                    await Task.Delay(delaySeconds * 1000, cancellationToken);
                }
                catch (Exception ex) when (retry == maxRetries || !IsRateLimitError(ex))
                {
                    if (!string.IsNullOrEmpty(currentConfig.FallbackModelId))
                    {
                        var fallbackConfig = SettingsManager.ConfiguredModels.FirstOrDefault(m => m.Id == currentConfig.FallbackModelId && m.IsEnabled);
                        if (fallbackConfig != null)
                        {
                            Serilog.Log.Warning(ex, ""Primary model {ModelName} failed. Falling back to {FallbackName}"", currentConfig.DisplayName, fallbackConfig.DisplayName);
                            onStatusUpdate($""Model {currentConfig.DisplayName} failed. Falling back to {fallbackConfig.DisplayName}..."");
                            currentConfig = fallbackConfig;
                            break; // break retry loop to try fallback
                        }
                    }
                    throw; // No fallback or fallback failed
                }
            }
        }
        
        throw new Exception(""No available models to execute the request."");
    }
"@

Set-Content "d:\Coding Sahayi\AgentContextManager.cs" $newCode
