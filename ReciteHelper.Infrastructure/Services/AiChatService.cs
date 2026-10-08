using LlmTornado;
using LlmTornado.Agents;
using LlmTornado.Chat.Models;
using OpenAI;
using OpenAI.Chat;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using System.ClientModel;

namespace ReciteHelper.Infrastructure.Services;

public sealed class AiChatService(
    HostedModelService hostedModelService,
    IConfigService configService,
    IPiModelService piModelService) : IAiChatService
{
    public async Task<string> RunAsync(string deepSeekKey, string prompt, string? instructions = null)
    {
        var config = await configService.LoadAsync();
        var accessMode = ModelAccess.Resolve(config);

        if (accessMode == ModelAccessMode.PiOAuth)
            return await piModelService.RunChatAsync(config.PiOAuthProvider!, config.PiOAuthModel!, prompt, instructions);

        if (accessMode == ModelAccessMode.OpenRouter)
            return await RunOpenRouterAsync(config, prompt, instructions);

        if (accessMode != ModelAccessMode.DeepSeekAndQwen)
            return await hostedModelService.RunChatAsync(prompt, instructions);

        var key = string.IsNullOrWhiteSpace(deepSeekKey) ? config.DeepSeekKey! : deepSeekKey;

        var api = new TornadoApi(key);
        var agent = new TornadoAgent(
            client: api,
            model: ChatModel.DeepSeek.Models.Chat,
            name: "ArchitectBot",
            instructions: instructions ?? "You are an assistant who is good at extracting knowledge.");

        var response = await agent.Run(prompt);
        return response.Messages.Last().Content ?? string.Empty;
    }

    private static async Task<string> RunOpenRouterAsync(
        ConfigOptions config,
        string prompt,
        string? instructions)
    {
        var client = new OpenAIClient(
                new ApiKeyCredential(config.OpenRouterKey!),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri("https://openrouter.ai/api/v1")
                })
            .GetChatClient(ResolveChatModel(config));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(instructions ?? "You are an assistant who is good at extracting knowledge."),
            new UserChatMessage(prompt)
        };
        var response = await client.CompleteChatAsync(messages, new ChatCompletionOptions
        {
            Temperature = 0.3f
        });

        return string.Concat(response.Value.Content.Select(part => part.Text));
    }

    private static string ResolveChatModel(ConfigOptions config)
    {
        return string.IsNullOrWhiteSpace(config.OpenRouterChatModel)
            ? "deepseek/deepseek-v3.2"
            : config.OpenRouterChatModel.Trim();
    }
}
