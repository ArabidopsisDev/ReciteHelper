using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReciteHelper.Infrastructure.OAuth;

// Text-request wire formats ported from pi-ai 1.1.0 api/* (MIT).
internal sealed class PiChatClient(HttpClient client, TimeProvider clock)
{
    internal async Task<string> CompleteAsync(PiModelDefinition model, JsonObject credential,
        string prompt, string? instructions, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var request = BuildRequest(model, credential, prompt, instructions);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw OAuthHttp.Failure(response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var json = await JsonNode.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false) as JsonObject
                ?? throw new InvalidOperationException("模型返回了无效的 JSON。");
            return ParseJsonResponse(json, model.Api);
        }
        return await ParseStreamAsync(stream, model.Api, timeout.Token).ConfigureAwait(false);
    }

    internal HttpRequestMessage BuildRequest(PiModelDefinition model, JsonObject credential, string prompt, string? instructions)
    {
        var access = OAuthHttp.Required(credential, "access");
        var system = instructions ?? "You are an assistant who is good at extracting knowledge.";
        var baseUrl = model.Provider == "github-copilot" ? PiOAuthClient.CopilotBaseUrl(credential) : model.BaseUrl.TrimEnd('/');
        var uri = model.Api switch
        {
            "anthropic-messages" => baseUrl + (baseUrl.EndsWith("/v1") ? "/messages" : "/v1/messages"),
            "openai-completions" => baseUrl + "/chat/completions",
            "openai-responses" => baseUrl + "/responses",
            "openai-codex-responses" => baseUrl.EndsWith("/codex/responses") ? baseUrl
                : baseUrl.EndsWith("/codex") ? baseUrl + "/responses" : baseUrl + "/codex/responses",
            "pi-messages" => baseUrl + "/messages",
            _ => throw new InvalidOperationException("当前模型使用的文本协议尚不支持。")
        };
        var request = new HttpRequestMessage(HttpMethod.Post, OAuthHttp.TrustedHttps(uri));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.UserAgent.ParseAdd("ReciteHelper/5.0");
        if (model.Headers is not null)
            foreach (var (name, value) in model.Headers)
            {
                request.Headers.Remove(name);
                request.Headers.TryAddWithoutValidation(name, value);
            }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (model.Provider == "github-copilot")
        {
            request.Headers.TryAddWithoutValidation("X-Initiator", "user");
            request.Headers.TryAddWithoutValidation("Openai-Intent", "conversation-edits");
        }
        var body = new JsonObject { ["model"] = model.Id, ["stream"] = true };
        var reserve = Math.Min(4096, Math.Max(16, model.ContextWindow / 20));
        var available = model.ContextWindow - prompt.EnumerateRunes().Count() - system.EnumerateRunes().Count() - reserve;
        if (model.ContextWindow > 0 && available <= 0) throw new InvalidOperationException("输入资料超过当前模型的上下文容量，请缩短资料或选择其他模型。");
        var maxTokens = Math.Max(1, Math.Min(model.MaxTokens, model.ContextWindow > 0 ? available : model.MaxTokens));
        var offDisabled = model.ThinkingLevelMap is not null && model.ThinkingLevelMap.TryGetPropertyValue("off", out var offValue) && offValue is null;
        var off = OAuthHttp.Text(model.ThinkingLevelMap, "off") ?? "none";
        if (model.Api == "anthropic-messages")
        {
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            body["max_tokens"] = maxTokens;
            var messages = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt });
            var systems = new JsonArray();
            var betas = new List<string>();
            if (model.Provider == "anthropic")
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", "claude-cli/2.1.280");
                request.Headers.TryAddWithoutValidation("x-app", "cli");
                betas.AddRange(["claude-code-20250219", "oauth-2025-04-20"]);
                systems.Add(new JsonObject { ["type"] = "text", ["text"] = "You are Claude Code, Anthropic's official CLI for Claude." });
            }
            if (model.Provider == "openrouter")
            {
                request.Headers.Authorization = null;
                request.Headers.TryAddWithoutValidation("x-api-key", access);
            }
            systems.Add(new JsonObject { ["type"] = "text", ["text"] = system });
            body["system"] = systems;
            body["messages"] = messages;
            if (model.Compat?["supportsMidConvoEffort"]?.GetValue<bool>() == true)
            {
                betas.AddRange(["mid-conversation-output-config-2026-07-01", "thinking-binding-controls-2026-08-01"]);
                messages.Add(new JsonObject { ["role"] = "system", ["content"] = new JsonArray(), ["output_config"] = new JsonObject { ["effort"] = "high" } });
                body["thinking"] = new JsonObject { ["type"] = "adaptive", ["display"] = "summarized", ["block_binding"] = new JsonObject { ["prefix_mismatch_behavior"] = "drop_block" } };
                body["output_config"] = new JsonObject { ["effort"] = "high" };
            }
            else if (model.Reasoning && !offDisabled) body["thinking"] = new JsonObject { ["type"] = "disabled" };
            if (betas.Count > 0 && !request.Headers.Contains("anthropic-beta"))
                request.Headers.TryAddWithoutValidation("anthropic-beta", string.Join(',', betas));
        }
        else if (model.Api == "openai-completions")
        {
            body["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = prompt });
            body[OAuthHttp.Text(model.Compat, "maxTokensField") ?? "max_completion_tokens"] = maxTokens;
            if (model.Compat?["supportsDeveloperRole"]?.GetValue<bool>() == true && model.Reasoning)
                body["messages"]![0]!["role"] = "developer";
        }
        else if (model.Api == "pi-messages")
        {
            body.Remove("stream");
            body["context"] = new JsonObject { ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = prompt, ["timestamp"] = clock.GetUtcNow().ToUnixTimeMilliseconds() }) };
            body["options"] = new JsonObject { ["maxTokens"] = maxTokens };
        }
        else
        {
            body["store"] = false;
            body["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = prompt }) });
            if (model.Api == "openai-codex-responses")
            {
                body["instructions"] = system;
                body["text"] = new JsonObject { ["verbosity"] = "low" };
                body["include"] = new JsonArray("reasoning.encrypted_content");
                body["tool_choice"] = "auto";
                body["parallel_tool_calls"] = true;
                request.Headers.TryAddWithoutValidation("originator", "ReciteHelper");
                request.Headers.TryAddWithoutValidation("chatgpt-account-id", OAuthHttp.AccountId(access));
                request.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=experimental");
            }
            else
            {
                var role = model.Compat?["supportsDeveloperRole"]?.GetValue<bool>() == false ? "system" : "developer";
                body["input"]!.AsArray().Insert(0, new JsonObject { ["role"] = role, ["content"] = system });
                // Sign in with ChatGPT rejects max_output_tokens, temperature,
                // prompt_cache_retention and prompt_cache_options.
                if (model.Provider != "openai" && model.Compat?["supportsMaxOutputTokens"]?.GetValue<bool>() != false)
                    body["max_output_tokens"] = maxTokens;
                if (model.Provider == "xai") body["include"] = new JsonArray("reasoning.encrypted_content");
            }
            if (model.Reasoning && !offDisabled && model.Provider != "github-copilot") body["reasoning"] = new JsonObject { ["effort"] = off };
        }
        var sampling = new JsonObject();
        if (model.SamplingParams is not null)
            foreach (var (key, value) in model.SamplingParams) sampling[key] = value?.DeepClone();
        if (model.SamplingParamsByThinkingLevel?["off"] is JsonObject level)
            foreach (var (key, value) in level) sampling[key] = value?.DeepClone();
        foreach (var (key, value) in sampling) body[key] = value?.DeepClone();
        request.Content = JsonContent.Create(body, options: PiModelCatalog.JsonOptions);
        return request;
    }

    private static string TextBlocks(JsonNode? content) => content is JsonArray array
        ? string.Concat(array.OfType<JsonObject>().Where(b => OAuthHttp.Text(b, "type") is "text" or "output_text").Select(b => OAuthHttp.Text(b, "text")))
        : content is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    private static string ResponseText(JsonObject response) => response["output"] is JsonArray array
        ? string.Concat(array.OfType<JsonObject>().Select(item => TextBlocks(item["content"]))) : "";
    private static string ValidateText(string text) => !string.IsNullOrWhiteSpace(text) ? text
        : throw new InvalidOperationException("模型没有返回文本，请检查模型权限或更换模型。");
    private static void ValidateStop(string? reason, string api)
    {
        var success = api == "anthropic-messages" ? reason is "end_turn" or "stop_sequence"
            : api is "openai-responses" or "openai-codex-responses" ? reason == "completed" : reason == "stop";
        if (!success) throw new InvalidOperationException(reason is "length" or "max_tokens" or "incomplete"
            ? "模型响应被截断，请选择输出容量更大的模型或缩短资料。" : "模型请求未正常完成，请检查服务权限、配额或重试。");
    }
    private static string ParseJsonResponse(JsonObject response, string api)
    {
        if (api == "anthropic-messages")
        { ValidateStop(OAuthHttp.Text(response, "stop_reason"), api); return ValidateText(TextBlocks(response["content"])); }
        if (api == "openai-completions")
        {
            var choice = response["choices"]?[0];
            ValidateStop(OAuthHttp.Text(choice, "finish_reason"), api);
            return ValidateText(TextBlocks(choice?["message"]?["content"]));
        }
        ValidateStop(OAuthHttp.Text(response, "status"), api);
        return ValidateText(ResponseText(response));
    }

    internal static async Task<string> ParseStreamAsync(Stream stream, string api, CancellationToken token)
    {
        var blocks = new SortedDictionary<int, StringBuilder>();
        string? stop = null;
        StringBuilder Block(int index) { if (!blocks.TryGetValue(index, out var block)) blocks[index] = block = new(); return block; }
        string Result() => ValidateText(string.Concat(blocks.Values.Select(block => block.ToString())));
        await foreach (var frame in ReadSseAsync(stream, token).ConfigureAwait(false))
        {
            if (frame == "[DONE]") { ValidateStop(stop, api); return Result(); }
            JsonObject packet;
            try { packet = JsonNode.Parse(frame) as JsonObject ?? throw new JsonException(); }
            catch (JsonException) { throw new InvalidOperationException("模型返回了无效的流数据。"); }
            var type = OAuthHttp.Text(packet, "type");
            if (type is "error" or "response.failed" or "response.incomplete")
                throw new InvalidOperationException("模型请求失败或响应被截断，请检查服务状态、账号配额或重试。");
            if (api == "anthropic-messages")
            {
                var index = (int)OAuthHttp.Number(packet, "index");
                if (type == "content_block_start" && OAuthHttp.Text(packet["content_block"], "type") == "text")
                    Block(index).Append(OAuthHttp.Text(packet["content_block"], "text"));
                if (type == "content_block_delta" && OAuthHttp.Text(packet["delta"], "type") == "text_delta")
                    Block(index).Append(OAuthHttp.Text(packet["delta"], "text"));
                if (type == "message_delta") stop = OAuthHttp.Text(packet["delta"], "stop_reason") ?? stop;
                if (type == "message_stop") { ValidateStop(stop, api); return Result(); }
            }
            else if (api == "openai-completions")
            {
                if (packet["choices"] is not JsonArray choices || choices.Count == 0) continue;
                var choice = choices[0];
                Block(0).Append(TextBlocks(choice?["delta"]?["content"]));
                stop = OAuthHttp.Text(choice, "finish_reason") ?? stop;
            }
            else if (api == "pi-messages")
            {
                var index = (int)OAuthHttp.Number(packet, "contentIndex");
                if (type == "text_delta") Block(index).Append(OAuthHttp.Text(packet, "delta"));
                if (type == "text_end" && OAuthHttp.Text(packet, "content") is { } content)
                { Block(index).Clear(); Block(index).Append(content); }
                if (type == "done") { ValidateStop(OAuthHttp.Text(packet, "reason"), api); return Result(); }
            }
            else
            {
                var index = (int)OAuthHttp.Number(packet, "output_index") * 1000 + (int)OAuthHttp.Number(packet, "content_index");
                if (type == "response.output_text.delta") Block(index).Append(OAuthHttp.Text(packet, "delta"));
                if (type == "response.output_text.done" && Block(index).Length == 0) Block(index).Append(OAuthHttp.Text(packet, "text"));
                if (type == "response.completed")
                {
                    var response = packet["response"] as JsonObject ?? throw new InvalidOperationException("模型响应缺少完成状态。");
                    ValidateStop(OAuthHttp.Text(response, "status"), api);
                    if (blocks.Count == 0) Block(0).Append(ResponseText(response));
                    return Result();
                }
            }
        }
        throw new InvalidOperationException("模型连接在完成响应前中断，请重试。");
    }

    private static async IAsyncEnumerable<string> ReadSseAsync(Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var data = new List<string>();
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Count > 0) { yield return string.Join('\n', data); data.Clear(); }
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Add(line[5..].TrimStart(' '));
        }
        if (data.Count > 0) yield return string.Join('\n', data);
    }
}
