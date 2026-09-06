$code = Get-Content "d:\Coding Sahayi\AgentContextManager.cs" -Raw

# Replace InitializeClient
$newCode = $code -replace '(?s)private void InitializeClient\(\).*?_localApiClient = localClient.GetChatClient\(localModel\);\s*\}', @"
    private ChatClient GetChatClient(CodingSahayi.Data.ModelEndpointConfig config)
    {
        var endpoint = config.BaseUrl;
        if (!endpoint.EndsWith("/")) endpoint += "/";
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint), NetworkTimeout = TimeSpan.FromMinutes(10) };
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(config.ApiKey ?? ""), options);
        return client.GetChatClient(config.ModelIdentifier);
    }

    private void InitializeClient()
    {
        // Clients are now created dynamically per request based on ModelEndpointConfig
    }
"@

# Helper to find config
$newCode = $newCode -replace '(?s)public async Task<bool> IsLocalAvailableAsync\(\)\s*\{.*?\}', @"
    public async Task<bool> IsLocalAvailableAsync()
    {
        try
        {
            var localConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
            if (localConfig == null) return false;
            
            var client = GetChatClient(localConfig);
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            var pingHistory = new List<ChatMessage> { new UserChatMessage("Respond with exactly one word: pong.") };
            await client.CompleteChatAsync(pingHistory, cancellationToken: cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
"@

# Update PlanAndRouteTask
$newCode = $newCode -replace '(?s)public async Task<\(string route, string plan\)> PlanAndRouteTask.*?\}', @"
    public async Task<(string route, string plan)> PlanAndRouteTask(string userPrompt, System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            var localConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
            if (localConfig == null) return ("API", "");
            var client = GetChatClient(localConfig);

            using var routerCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            routerCts.CancelAfter(TimeSpan.FromSeconds(15));
            
            var routerSystem = new SystemChatMessage(
                "You are a task complexity classifier. Analyze the user's task and respond with ONLY a JSON object.\n" +
                "Evaluate based on: Does it need multi-file edits? Does it need architectural reasoning? Does it need tool calls (reading/writing files, running commands)?\n" +
                "Simple tasks: greetings, Q&A, explanations, single-concept questions, formatting.\n" +
                "Complex tasks: code generation, debugging, refactoring, multi-step builds, file operations.\n" +
                "Output format: {\""Plan\"": \""brief 1-sentence plan\"", \""ComplexityScore\"": <1-10>}");
            var routerUser = new UserChatMessage(userPrompt);
            var routerHistory = new List<ChatMessage> { routerSystem, routerUser };
            
            var routerOptions = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };
            
            var completion = await client.CompleteChatAsync(routerHistory, routerOptions, routerCts.Token);
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
"@

Set-Content "d:\Coding Sahayi\AgentContextManager.cs" $newCode
