using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks how the API settings are read from environment variables.</summary>
public class AiSettingsTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void Missing_key_explains_what_to_set()
    {
        Assert.False(AiSettings.TryLoad(Env(), out AiSettings? settings, out string problem));
        Assert.Null(settings);
        Assert.Equal("No API key is set.", problem);
        Assert.False(AiSettings.HasApiKey(Env()));
    }

    [Fact]
    public void Key_alone_uses_the_default_endpoint_and_model()
    {
        Assert.True(AiSettings.TryLoad(Env(("BANKING_AI_API_KEY", "k")), out AiSettings? settings, out _));

        Assert.Equal(new Uri("https://api.openai.com/v1/chat/completions"), settings!.ChatCompletionsUri);
        Assert.Equal(AiSettings.DefaultModel, settings.Model);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.Timeout);
    }

    [Theory]
    [InlineData("BANKING_AI_API_KEY")]
    [InlineData("OPENAI_API_KEY")]
    public void Either_key_variable_works_and_status_shows_which_was_used(string variable)
    {
        Assert.True(AiSettings.HasApiKey(Env((variable, "k"))));
        Assert.True(AiSettings.TryLoad(Env((variable, "k")), out AiSettings? settings, out _));
        Assert.Equal($"from {variable}", settings!.ApiKeySource);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/chat/completions")]
    [InlineData("https://example.test/openai/v1/", "https://example.test/openai/v1/chat/completions")]
    [InlineData("https://example.test/v1/chat/completions", "https://example.test/v1/chat/completions")]
    public void Endpoint_and_model_are_configurable(string endpoint, string expected)
    {
        Assert.True(AiSettings.TryLoad(
            Env(("BANKING_AI_API_KEY", "k"), ("BANKING_AI_ENDPOINT", endpoint), ("BANKING_AI_MODEL", "my-model"),
                ("BANKING_AI_TIMEOUT_SECONDS", "90")),
            out AiSettings? settings, out _));

        Assert.Equal(new Uri(expected), settings!.ChatCompletionsUri);
        Assert.Equal("my-model", settings.Model);
        Assert.Equal(TimeSpan.FromSeconds(90), settings.Timeout);
    }

    [Theory]
    [InlineData("BANKING_AI_ENDPOINT", "not a url")]
    [InlineData("BANKING_AI_ENDPOINT", "ftp://example.test")]
    [InlineData("BANKING_AI_TIMEOUT_SECONDS", "0")]
    [InlineData("BANKING_AI_TIMEOUT_SECONDS", "soon")]
    public void Invalid_settings_are_explained(string name, string value)
    {
        Assert.False(AiSettings.TryLoad(Env(("BANKING_AI_API_KEY", "k"), (name, value)), out _, out string problem));
        Assert.StartsWith(name, problem);
    }

    [Fact]
    public void The_key_is_not_included_when_settings_are_printed()
    {
        AiSettings.TryLoad(Env(("BANKING_AI_API_KEY", "secret-key-value")), out AiSettings? settings, out _);

        Assert.DoesNotContain("secret-key-value", settings!.ToString());
    }
}
