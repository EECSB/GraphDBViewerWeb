using System;

namespace GraphDBViewerWeb.Code;

///<summary>Text cleanup for model output, shared by the AI features ("Ask AI" and knowledge-graph
///generation) so the fence handling exists once. NlQueryPrompt.CleanQuery delegates here.</summary>
public static class LlmText
{
    ///<summary>
    ///Strips a surrounding markdown code fence (```lang … ```) and trims, so a fenced reply still
    ///parses cleanly whether it is a query or JSON.
    ///</summary>
    public static string StripMarkdownFences(string modelOutput)
    {
        if (string.IsNullOrWhiteSpace(modelOutput))
            return string.Empty;

        var text = modelOutput.Trim();

        if (!text.StartsWith("```"))
            return text;

        //Drop the opening fence line (``` optionally followed by a language tag).
        int firstNewline = text.IndexOf('\n');

        if (firstNewline < 0)
            return text;

        text = text.Substring(firstNewline + 1);

        //Drop the closing fence.
        int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);

        if (lastFence >= 0)
            text = text.Substring(0, lastFence);

        return text.Trim();
    }
}
