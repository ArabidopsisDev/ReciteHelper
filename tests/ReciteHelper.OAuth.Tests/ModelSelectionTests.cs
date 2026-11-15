using System.Xml.Serialization;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Infrastructure.Configuration;

namespace ReciteHelper.OAuth.Tests;

public sealed class ModelSelectionTests
{
    [Theory]
    [InlineData(ModelAccessMode.DeepSeekAndQwen)]
    [InlineData(ModelAccessMode.PiOAuth)]
    [InlineData(ModelAccessMode.OpenRouter)]
    [InlineData(ModelAccessMode.Hosted)]
    public void ExplicitChoiceOverridesAutomaticPriorityAndSurvivesSerialization(ModelAccessMode choice)
    {
        var config = new ConfigOptions { DeepSeekKey = "d", QwenKey = "q", PiOAuthProvider = "provider", PiOAuthModel = "model", OpenRouterKey = "o", HostedLicenseId = "h", PreferredTextProvider = choice };
        var serializer = new XmlSerializer(typeof(ConfigOptions));
        using var writer = new StringWriter();
        serializer.Serialize(writer, config);
        var restored = (ConfigOptions)serializer.Deserialize(new StringReader(writer.ToString()))!;
        Assert.Equal(choice, ModelAccess.Resolve(restored));
        Assert.Equal("d", restored.DeepSeekKey);
        Assert.Equal("model", restored.PiOAuthModel);
    }

    [Fact]
    public void MissingSelectedServiceRequiresConfiguration()
    {
        Assert.Equal(ModelAccessMode.None, ModelAccess.Resolve(new ConfigOptions { PreferredTextProvider = ModelAccessMode.PiOAuth, DeepSeekKey = "d" }));
        var direct = new ConfigOptions { DeepSeekKey = "d" };
        Assert.Equal(ModelAccessMode.DeepSeekAndQwen, ModelAccess.Resolve(direct));
        Assert.Equal(ModelAccessMode.None, ModelAccess.ResolveEmbedding(direct));
    }

    [Fact]
    public async Task SwitchingModelsPreservesEnvironmentReference()
    {
        const string variable = "RECITE_TEST_DEEPSEEK";
        var original = Environment.GetEnvironmentVariable(variable);
        var directory = Path.Combine(Path.GetTempPath(), "ModelConfigTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Config.xml");
        try
        {
            Environment.SetEnvironmentVariable(variable, "test-secret");
            await File.WriteAllTextAsync(path, $"<Config><DeepSeekKey>%Environment.GetEnvironmentVariable(\"{variable}\")%</DeepSeekKey><PiOAuthProvider>provider</PiOAuthProvider><PiOAuthModel>model</PiOAuthModel></Config>");
            var service = new ConfigService(path);
            var config = await service.LoadAsync();
            Assert.Equal("test-secret", config.DeepSeekKey);
            config.PreferredTextProvider = ModelAccessMode.DeepSeekAndQwen;
            await service.SaveAsync(config);
            var saved = await File.ReadAllTextAsync(path);
            Assert.Contains(variable, saved);
            Assert.DoesNotContain("test-secret", saved);
            var restored = await service.LoadAsync();
            Assert.Equal(ModelAccessMode.DeepSeekAndQwen, ModelAccess.Resolve(restored));
            Assert.Equal("provider", restored.PiOAuthProvider);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
            Directory.Delete(directory, true);
        }
    }
}
