using OpenAI;
using OpenAI.Chat;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI.Embeddings;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Core.DTOs;

namespace ReciteHelper.Infrastructure.Services
{
    public class KnowledgeBaseService(
        IConfigService cfgService,
        IAiChatService aiChatService,
        HostedModelService hostedModelService) : IKnowledgeBaseService
    {
        private readonly int _chunkSize = 1500;
        private readonly int _overlap = 50;

        private ChatClient? _chatClient;
        private EmbeddingClient? _embedClient;

        private const int MaxConcurrentRequests = 4;
        private const int BatchSize = 10;

        private IConfigService configService = cfgService;

        public async Task<FileVectorStore> Build(string projectPath, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("知识库构建失败：题库文本为空。");

            var slices = Slice(text);
            if (slices.Count == 0)
                throw new InvalidOperationException("知识库构建失败：未切分出有效文本。");

            var cfg = await configService.LoadAsync();
            if (ModelAccess.ResolveEmbedding(cfg) == ModelAccessMode.None)
                throw new InvalidOperationException("知识库需要独立的 Qwen Key、OpenRouter Key 或托管向量服务；OAuth 文本生成功能仍可使用。");
            var src = new CancellationTokenSource();
            _chatClient = null;
            _embedClient = null;

            if (ModelAccess.Resolve(cfg) == ModelAccessMode.DeepSeekAndQwen)
            {
                _chatClient = new OpenAIClient(new ApiKeyCredential(cfg.DeepSeekKey!), new OpenAIClientOptions
                {
                    Endpoint = new Uri("https://api.deepseek.com")
                }).GetChatClient("deepseek-v4-flash");
            }
            else if (ModelAccess.Resolve(cfg) == ModelAccessMode.OpenRouter)
            {
                _chatClient = CreateOpenRouterChatClient(cfg);
            }

            _embedClient = ModelAccess.ResolveEmbedding(cfg) switch
            {
                ModelAccessMode.DeepSeekAndQwen => CreateQwenEmbeddingClient(cfg.QwenKey!),
                ModelAccessMode.OpenRouter => CreateOpenRouterEmbeddingClient(cfg),
                _ => null
            };

            var cluster = await ClusterAsync(slices, src.Token);
            var embed = await EmbedAsync(cluster, src.Token);
            var elements = BuildVectorEntries(
                cluster,
                embed,
                src.Token,
                ResolveEmbeddingModelId(cfg));
            var fvs = BuildVectorStore(projectPath, elements);

            return fvs;
        }

        public async Task<IReadOnlyList<KnowledgeBaseMatch>> SearchAsync(
            FileVectorStore store,
            string query,
            int topK,
            CancellationToken cancellationToken = default)
        {
            if (store.Entries.Count == 0 || string.IsNullOrWhiteSpace(query) || topK <= 0)
                return [];

            var cfg = await configService.LoadAsync();
            await EnsureCompatibleEmbeddingsAsync(store, cfg, cancellationToken);
            var queryVector = ModelAccess.ResolveEmbedding(cfg) switch
            {
                ModelAccessMode.DeepSeekAndQwen =>
                    await GenerateEmbeddingAsync(CreateQwenEmbeddingClient(cfg.QwenKey!), query.Trim(), cancellationToken),
                ModelAccessMode.OpenRouter =>
                    await GenerateEmbeddingAsync(CreateOpenRouterEmbeddingClient(cfg), query.Trim(), cancellationToken),
                ModelAccessMode.Hosted =>
                    (await hostedModelService.EmbedTextsAsync([query.Trim()], cancellationToken))[0],
                _ => throw new InvalidOperationException("尚未配置可用的模型服务。")
            };

            return store.Search(queryVector, topK)
                .Select(result => new KnowledgeBaseMatch(
                    CreateMatchTitle(result.Entry),
                    result.Entry.Text,
                    result.Score))
                .ToList();
        }

        public async Task<IReadOnlyList<float[]>> EmbedTextsAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            var normalizedTexts = texts
                .Select(text => text?.Trim() ?? string.Empty)
                .ToList();
            if (normalizedTexts.Count == 0)
                return [];
            if (normalizedTexts.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("待向量化文本不能为空。", nameof(texts));

            var cfg = await configService.LoadAsync();
            var accessMode = ModelAccess.ResolveEmbedding(cfg);
            if (accessMode == ModelAccessMode.Hosted)
                return await hostedModelService.EmbedTextsAsync(normalizedTexts, cancellationToken);

            var embedClient = accessMode switch
            {
                ModelAccessMode.DeepSeekAndQwen => CreateQwenEmbeddingClient(cfg.QwenKey!),
                ModelAccessMode.OpenRouter => CreateOpenRouterEmbeddingClient(cfg),
                _ => throw new InvalidOperationException("尚未配置可用的模型服务。")
            };
            var results = new float[normalizedTexts.Count][];
            var batches = normalizedTexts
                .Select((text, index) => new { Text = text, Index = index })
                .Chunk(BatchSize)
                .ToList();

            using var semaphore = new SemaphoreSlim(MaxConcurrentRequests);
            var tasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var response = await embedClient.GenerateEmbeddingsAsync(
                        batch.Select(item => item.Text).ToList(),
                        options: null,
                        cancellationToken);

                    for (var index = 0; index < batch.Length; index++)
                        results[batch[index].Index] = response.Value[index].ToFloats().ToArray();
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results;
        }

        private static EmbeddingClient CreateQwenEmbeddingClient(string qwenKey)
        {
            return new OpenAIClient(
                new ApiKeyCredential(qwenKey),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri("https://dashscope.aliyuncs.com/compatible-mode/v1")
                }).GetEmbeddingClient("text-embedding-v4");
        }

        private static ChatClient CreateOpenRouterChatClient(ConfigOptions config)
        {
            var model = string.IsNullOrWhiteSpace(config.OpenRouterChatModel)
                ? "deepseek/deepseek-v3.2"
                : config.OpenRouterChatModel.Trim();

            return new OpenAIClient(
                new ApiKeyCredential(config.OpenRouterKey!),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri("https://openrouter.ai/api/v1")
                }).GetChatClient(model);
        }

        private static EmbeddingClient CreateOpenRouterEmbeddingClient(ConfigOptions config)
        {
            return new OpenAIClient(
                new ApiKeyCredential(config.OpenRouterKey!),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri("https://openrouter.ai/api/v1")
                }).GetEmbeddingClient(ResolveOpenRouterEmbeddingModel(config));
        }

        private static async Task<float[]> GenerateEmbeddingAsync(
            EmbeddingClient embedClient,
            string text,
            CancellationToken cancellationToken)
        {
            var response = await embedClient.GenerateEmbeddingsAsync(
                [text],
                options: null,
                cancellationToken);
            return response.Value[0].ToFloats().ToArray();
        }

        private async Task EnsureCompatibleEmbeddingsAsync(
            FileVectorStore store,
            ConfigOptions config,
            CancellationToken cancellationToken)
        {
            var currentModel = ResolveEmbeddingModelId(config);
            var storedModel = store.Entries
                .Select(entry => entry.EmbeddingModel)
                .FirstOrDefault(model => !string.IsNullOrWhiteSpace(model));

            var isLegacyStore = string.IsNullOrWhiteSpace(storedModel);
            var switchingLegacyStoreToOpenRouter =
                isLegacyStore && ModelAccess.ResolveEmbedding(config) == ModelAccessMode.OpenRouter;
            var changedKnownModel =
                !isLegacyStore && !string.Equals(storedModel, currentModel, StringComparison.OrdinalIgnoreCase);

            if (!switchingLegacyStoreToOpenRouter && !changedKnownModel)
                return;

            var vectors = await EmbedTextsAsync(
                store.Entries.Select(entry => entry.Text).ToList(),
                cancellationToken);
            store.ReplaceVectors(vectors, currentModel);
        }

        private static string ResolveEmbeddingModelId(ConfigOptions config)
        {
            return ModelAccess.ResolveEmbedding(config) switch
            {
                ModelAccessMode.DeepSeekAndQwen => "dashscope/text-embedding-v4",
                ModelAccessMode.OpenRouter => $"openrouter/{ResolveOpenRouterEmbeddingModel(config)}",
                ModelAccessMode.Hosted => "recitehelper/hosted",
                _ => "unconfigured"
            };
        }

        private static string ResolveOpenRouterEmbeddingModel(ConfigOptions config)
        {
            return string.IsNullOrWhiteSpace(config.OpenRouterEmbeddingModel)
                ? "baai/bge-m3"
                : config.OpenRouterEmbeddingModel.Trim();
        }

        private static string CreateMatchTitle(VectorEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.Semantics.Summary))
                return entry.Semantics.Summary;

            var tags = string.Join("、", entry.Semantics.Tags.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!string.IsNullOrWhiteSpace(tags))
                return tags;

            const int maxLength = 24;
            return entry.Text.Length <= maxLength
                ? entry.Text
                : $"{entry.Text[..maxLength]}...";
        }

        public List<string> Slice(string text)
        {
            var slices = SplitText(text);

            return slices.Select(x => x.Replace("\n", "").Trim()).ToList();
        }


        public List<string> SplitText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            text = CleanText(text);
            var paragraphs = SplitByParagraphs(text);

            var chunks = new List<string>();
            foreach (var para in paragraphs)
            {
                chunks.AddRange(SplitParagraph(para));
            }

            return chunks;
        }

        private string CleanText(string text)
        {
            text = Regex.Replace(text, @"\n\s*\n", "\n\n");
            text = text.Trim();
            return text;
        }

        private List<string> SplitByParagraphs(string text)
        {
            var paragraphs = new List<string>();
            var currentPara = new StringBuilder();

            using var reader = new StringReader(text);
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentPara.Length > 0)
                    {
                        paragraphs.Add(currentPara.ToString().Trim());
                        currentPara.Clear();
                    }
                }
                else
                {
                    currentPara.AppendLine(line);
                }
            }

            if (currentPara.Length > 0)
            {
                paragraphs.Add(currentPara.ToString().Trim());
            }

            return paragraphs;
        }

        private List<string> SplitParagraph(string paragraph)
        {
            if (paragraph.Length <= _chunkSize)
            {
                return new List<string> { paragraph };
            }

            var chunks = new List<string>();
            var sentences = SplitIntoSentences(paragraph);

            var currentChunk = new StringBuilder();

            foreach (var sentence in sentences)
            {
                if (currentChunk.Length + sentence.Length + 1 > _chunkSize && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());

                    string overlap = GetOverlap(currentChunk.ToString());
                    currentChunk.Clear();
                    currentChunk.Append(overlap);
                }

                currentChunk.Append(sentence);
            }

            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            return chunks;
        }

        private List<string> SplitIntoSentences(string text)
        {
            var separators = new[] { "。", "！", "？", "；", ".", "!", "?", ";" };

            var sentences = new List<string>();
            var currentSentence = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                currentSentence.Append(text[i]);

                foreach (var sep in separators)
                {
                    if (text[i].ToString() == sep)
                    {
                        sentences.Add(currentSentence.ToString());
                        currentSentence.Clear();
                        break;
                    }
                }
            }

            if (currentSentence.Length > 0)
            {
                sentences.Add(currentSentence.ToString());
            }

            return sentences;
        }

        private string GetOverlap(string chunk)
        {
            if (chunk.Length <= _overlap)
                return chunk;

            int start = chunk.Length - _overlap;

            int actualStart = chunk.Length;
            for (int i = start; i < chunk.Length; i++)
            {
                if (i > 0 && (chunk[i - 1] == '。' || chunk[i - 1] == '！' || chunk[i - 1] == '？'))
                {
                    actualStart = i;
                    break;
                }
            }

            if (actualStart >= chunk.Length)
                actualStart = start;

            return chunk.Substring(actualStart);
        }

        public async Task<Dictionary<Semantics, string>> ClusterAsync(List<string> chunks, CancellationToken cts = default)
        {
            var results = new ConcurrentDictionary<Semantics, string>();

            await Parallel.ForEachAsync(chunks,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentRequests, CancellationToken = cts },
                async (chunk, _) =>
                {
                    var segmentedSemantics = await ProcessChunkAsync(chunk);
                    foreach (var item in segmentedSemantics)
                    {
                        results.TryAdd(item.Item1, item.Item2);
                    }
                });

            return results.OrderBy(x => x.Key.Id).ToDictionary(x => x.Key, x => x.Value);
        }

        /// <summary>
        /// Process single chunk: segment by atomic knowledge points and generate semantics
        /// </summary>
        private async Task<List<(Semantics, string)>> ProcessChunkAsync(string chunk)
        {
            var prompt = BuildPrompt(chunk);

            const string instructions = "你是一个专业的文本分析专家。请完成以下任务：\n" +
                                        "1. 将输入的文本块按照知识点的原子性进行分割\n" +
                                        "2. 确保每个分割后的段落只表达一个独立、完整的知识点\n" +
                                        "3. 为每个知识点段落生成2-3个关键词标签\n" +
                                        "4. 生成不超过20字的单句摘要\n" +
                                        "5. 对于被截断的知识点，如果有明确的语义信息进行补全，否则直接丢弃" +
                                        "6. 返回JSON数组，每个元素包含text、tags、summary字段，无其他内容，不要添加markdown代码块结构";

            if (_chatClient is null)
            {
                var hostedResponse = await aiChatService.RunAsync(string.Empty, prompt, instructions);
                return ParseResponse(ExtractJsonContent(hostedResponse));
            }

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(instructions),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.3f,
                MaxOutputTokenCount = 66666
            });

            var json = ExtractJsonContent(response.Value.Content[0].Text);
            return ParseResponse(json);
        }

        private static string BuildPrompt(string chunk)
        {
            var sb = new StringBuilder();
            sb.AppendLine("请处理以下文本块：");
            sb.AppendLine();
            sb.AppendLine(chunk);
            sb.AppendLine();
            sb.AppendLine("返回JSON数组格式，示例：");
            sb.AppendLine("[");
            sb.AppendLine("  {\"text\": \"分割后的知识点1\", \"tags\": [\"标签1\", \"标签2\"], \"summary\": \"摘要\"},");
            sb.AppendLine("  {\"text\": \"分割后的知识点2\", \"tags\": [\"标签1\", \"标签2\"], \"summary\": \"摘要\"}");
            sb.AppendLine("]");

            return sb.ToString();
        }

        /// <summary>
        /// Parse response and return semantics-text pairs with auto-incremented IDs
        /// </summary>
        private static List<(Semantics, string)> ParseResponse(string json)
        {
            var result = new List<(Semantics, string)>();
            var idCounter = 0;

            using var doc = JsonDocument.Parse(json);
            var array = doc.RootElement.EnumerateArray().ToList();

            foreach (var item in array)
            {
                var text = item.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? ""
                    : "";

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var semantics = new Semantics
                {
                    Id = idCounter++,
                    Tags = item.TryGetProperty("tags", out var tags)
                        ? tags.EnumerateArray()
                            .Select(t => t.GetString() ?? "")
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .ToList()
                        : [],
                    Summary = item.TryGetProperty("summary", out var summary)
                        ? (summary.GetString() ?? "").Trim()
                        : ""
                };

                result.Add((semantics, text.Trim()));
            }


            return result;
        }

        private static string ExtractJsonContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "[]";

            var trimmed = content.Trim();
            var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
            if (fenceStart >= 0)
            {
                var jsonStart = trimmed.IndexOf('\n', fenceStart);
                if (jsonStart >= 0)
                {
                    var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                    trimmed = fenceEnd > jsonStart
                        ? trimmed[(jsonStart + 1)..fenceEnd].Trim()
                        : trimmed[(jsonStart + 1)..].Trim();
                }
            }

            var arrayStart = trimmed.IndexOf('[');
            var arrayEnd = trimmed.LastIndexOf(']');
            return arrayStart >= 0 && arrayEnd > arrayStart
                ? trimmed[arrayStart..(arrayEnd + 1)]
                : trimmed;
        }

        public async Task<Dictionary<Semantics, float[]>> EmbedAsync(
            Dictionary<Semantics, string> semanticChunks,
            CancellationToken cts)
        {
            if (semanticChunks.Count == 0) return [];

            var semanticsList = semanticChunks.Keys.ToList();
            var textsList = semanticChunks.Values.ToList();

            // Results array maintains index correspondence with input order
            var results = new float[textsList.Count][];

            var batches = textsList
                .Select((text, index) => new { text, index })
                .Chunk(BatchSize)
                .ToList();

            if (_embedClient is null)
            {
                var vectors = await EmbedTextsAsync(textsList, cts);
                var hostedResult = new Dictionary<Semantics, float[]>();
                for (var index = 0; index < semanticsList.Count; index++)
                    hostedResult[semanticsList[index]] = vectors[index];

                return hostedResult;
            }

            using var semaphore = new SemaphoreSlim(MaxConcurrentRequests);

            var tasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync(cts);
                try
                {
                    var batchTexts = batch.Select(b => b.text).ToList();
                    var response = await _embedClient.GenerateEmbeddingsAsync(batchTexts, options: null, cts);

                    for (int i = 0; i < batch.Length; i++)
                        results[batch[i].index] = response.Value[i].ToFloats().ToArray();
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Reconstruct the result dictionary maintaining Semantics-Embedding pairing
            var resultDictionary = new Dictionary<Semantics, float[]>();
            for (int i = 0; i < semanticsList.Count; i++)
            {
                resultDictionary[semanticsList[i]] = results[i];
            }

            return resultDictionary;
        }

        public List<VectorEntry> BuildVectorEntries(
            Dictionary<Semantics, string> semanticChunks,
            Dictionary<Semantics, float[]> vectors,
            CancellationToken cts,
            string? embeddingModel = null)
        {
            var entries = semanticChunks
                .Select((kvp, idx) => new VectorEntry
                {
                    Id = idx,
                    Semantics = new Semantics
                    {
                        Id = idx,
                        Tags = kvp.Key.Tags,
                        Summary = kvp.Key.Summary
                    },
                    Text = kvp.Value,
                    Vector = vectors[kvp.Key],
                    EmbeddingModel = embeddingModel,
                    SourceFile = "question-bank",
                    CreatedAt = DateTime.UtcNow
                })
                .ToList();

            Console.WriteLine($"知识库已构建：{entries.Count} 个向量。");
            return entries;
        }

        public FileVectorStore BuildVectorStore(string projectPath, List<VectorEntry> entries)
        {
            var fvs = new FileVectorStore(projectPath);
            fvs.AddRange(entries);
            return fvs;
        }

    }
}
