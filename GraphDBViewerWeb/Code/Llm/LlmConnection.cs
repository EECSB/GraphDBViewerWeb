namespace GraphDBViewerWeb.Code;

///<summary>
///A saved "AI model" connection used for natural-language → query generation. Stored in localStorage
///alongside the database connections. Bring-your-own-key: the user supplies the provider, endpoint and key.
///</summary>
public class LlmConnection
{
    public LlmConnection() { }

    public LlmConnection(LlmConnection copy)
    {
        Name = copy.Name;
        ProviderType = copy.ProviderType;
        BaseUrl = copy.BaseUrl;
        ApiKey = copy.ApiKey;
        Model = copy.Model;
        MaxTokens = copy.MaxTokens;
        Temperature = copy.Temperature;
    }

    public string Name { get; set; }

    ///<summary>Provider family: "Anthropic" | "OpenAI" | "Gemini" | "Custom". OpenAI, Gemini and Custom share the OpenAI-compatible adapter.</summary>
    public string ProviderType { get; set; } = "Anthropic";

    ///<summary>Base URL for OpenAI-compatible endpoints (e.g. http://localhost:11434/v1 for Ollama). Ignored for Anthropic.</summary>
    public string BaseUrl { get; set; }

    public string ApiKey { get; set; }

    ///<summary>Model id (e.g. claude-opus-4-8, gpt-4o-mini, or a local model name).</summary>
    public string Model { get; set; }

    ///<summary>
    ///Output token cap when the provider needs one (Anthropic sends it; the OpenAI-compatible adapter
    ///doesn't). Generous on purpose: it's a ceiling, not an allocation — you're billed for what the model
    ///actually writes — and a cap that's too low truncates the answer mid-sentence rather than costing less.
    ///The generated query is short, but a tool-using model spends this on its reasoning and tool calls too.
    ///</summary>
    public const int DefaultMaxTokens = 8192;

    public int MaxTokens { get; set; } = DefaultMaxTokens;

    ///<summary>Optional sampling temperature; null omits it (some newer models reject a non-default value).</summary>
    public double? Temperature { get; set; }
}
