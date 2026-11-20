using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks that a key typed at startup is sent to the provider the user chose.</summary>
public class ApiKeyPromptTests
{
    private static string? NothingSet(string name) => null;

    [Fact]
    public void A_gemini_key_uses_gemini_s_endpoint_and_model()
    {
        Func<string, string?> settings = ApiKeyPrompt.SettingsWith(new EnteredKey(AiProvider.Gemini, "gemini-key"), NothingSet);

        Assert.True(AiSettings.TryLoad(settings, out AiSettings? loaded, out _));
        Assert.Equal(new Uri("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"),
            loaded!.ChatCompletionsUri);
        Assert.Equal("gemini-3.8-flash", loaded.Model);
        Assert.Equal("gemini-key", loaded.ApiKey);
    }

    [Fact]
    public void An_openai_key_uses_openai_s_endpoint_and_model()
    {
        Func<string, string?> settings = ApiKeyPrompt.SettingsWith(new EnteredKey(AiProvider.OpenAi, "openai-key"), NothingSet);

        Assert.True(AiSettings.TryLoad(settings, out AiSettings? loaded, out _));
        Assert.Equal(new Uri("https://api.openai.com/v1/chat/completions"), loaded!.ChatCompletionsUri);
        Assert.Equal("gpt-4o-mini", loaded.Model);
    }

    [Fact]
    public void Other_settings_still_come_from_the_environment()
    {
        Func<string, string?> settings = ApiKeyPrompt.SettingsWith(
            new EnteredKey(AiProvider.Gemini, "gemini-key"),
            name => name == AiSettings.TimeoutVariable ? "90" : null);

        Assert.True(AiSettings.TryLoad(settings, out AiSettings? loaded, out _));
        Assert.Equal(TimeSpan.FromSeconds(90), loaded!.Timeout);
    }
}
