namespace ReciteHelper.Core.Configuration;

public enum ModelAccessMode
{
    None,
    DeepSeekAndQwen,
    OpenRouter,
    Hosted,
    PiOAuth
}

public static class ModelAccess
{
    public static ModelAccessMode Resolve(ConfigOptions? config)
    {
        if (config is null)
            return ModelAccessMode.None;

        if (!string.IsNullOrWhiteSpace(config.PiOAuthProvider) &&
            !string.IsNullOrWhiteSpace(config.PiOAuthModel))
            return ModelAccessMode.PiOAuth;

        if (!string.IsNullOrWhiteSpace(config.DeepSeekKey) &&
            !string.IsNullOrWhiteSpace(config.QwenKey))
            return ModelAccessMode.DeepSeekAndQwen;

        if (!string.IsNullOrWhiteSpace(config.OpenRouterKey))
            return ModelAccessMode.OpenRouter;

        if (!string.IsNullOrWhiteSpace(config.HostedLicenseId) ||
            !string.IsNullOrWhiteSpace(config.HostedLicenseCode))
            return ModelAccessMode.Hosted;

        return ModelAccessMode.None;
    }

    public static bool HasTextGeneration(ConfigOptions? config) => Resolve(config) != ModelAccessMode.None;

    public static ModelAccessMode ResolveEmbedding(ConfigOptions? config)
    {
        if (Resolve(config) != ModelAccessMode.PiOAuth)
            return Resolve(config);

        // pi's chat OAuth providers do not expose a common embedding API.
        if (!string.IsNullOrWhiteSpace(config!.QwenKey))
            return ModelAccessMode.DeepSeekAndQwen;
        if (!string.IsNullOrWhiteSpace(config.OpenRouterKey))
            return ModelAccessMode.OpenRouter;
        if (!string.IsNullOrWhiteSpace(config.HostedLicenseId) ||
            !string.IsNullOrWhiteSpace(config.HostedLicenseCode))
            return ModelAccessMode.Hosted;
        return ModelAccessMode.None;
    }
}
