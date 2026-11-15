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

        if (config.PreferredTextProvider != ModelAccessMode.None)
            return IsConfigured(config, config.PreferredTextProvider) ? config.PreferredTextProvider : ModelAccessMode.None;

        if (!string.IsNullOrWhiteSpace(config.PiOAuthProvider) &&
            !string.IsNullOrWhiteSpace(config.PiOAuthModel))
            return ModelAccessMode.PiOAuth;

        if (!string.IsNullOrWhiteSpace(config.DeepSeekKey))
            return ModelAccessMode.DeepSeekAndQwen;

        if (!string.IsNullOrWhiteSpace(config.OpenRouterKey))
            return ModelAccessMode.OpenRouter;

        if (!string.IsNullOrWhiteSpace(config.HostedLicenseId) ||
            !string.IsNullOrWhiteSpace(config.HostedLicenseCode))
            return ModelAccessMode.Hosted;

        return ModelAccessMode.None;
    }

    public static bool HasTextGeneration(ConfigOptions? config) => Resolve(config) != ModelAccessMode.None;

    public static bool IsConfigured(ConfigOptions config, ModelAccessMode mode) => mode switch
    {
        ModelAccessMode.PiOAuth => !string.IsNullOrWhiteSpace(config.PiOAuthProvider) && !string.IsNullOrWhiteSpace(config.PiOAuthModel),
        ModelAccessMode.DeepSeekAndQwen => !string.IsNullOrWhiteSpace(config.DeepSeekKey),
        ModelAccessMode.OpenRouter => !string.IsNullOrWhiteSpace(config.OpenRouterKey),
        ModelAccessMode.Hosted => !string.IsNullOrWhiteSpace(config.HostedLicenseId) || !string.IsNullOrWhiteSpace(config.HostedLicenseCode),
        _ => false
    };

    public static ModelAccessMode ResolveEmbedding(ConfigOptions? config)
    {
        if (config is null) return ModelAccessMode.None;
        var selected = Resolve(config);
        if (selected is ModelAccessMode.OpenRouter or ModelAccessMode.Hosted) return selected;
        if (!string.IsNullOrWhiteSpace(config.QwenKey))
            return ModelAccessMode.DeepSeekAndQwen;
        if (!string.IsNullOrWhiteSpace(config.OpenRouterKey))
            return ModelAccessMode.OpenRouter;
        if (!string.IsNullOrWhiteSpace(config.HostedLicenseId) ||
            !string.IsNullOrWhiteSpace(config.HostedLicenseCode))
            return ModelAccessMode.Hosted;
        return ModelAccessMode.None;
    }
}
