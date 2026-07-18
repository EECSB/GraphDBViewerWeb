using System;
using System.Text;

namespace GraphDBViewerWeb.Code;

///<summary>
///Builds the system prompt that turns a natural-language request into a database query, grounded in the
///connected graph's schema, and cleans the model's reply. Pure and unit-tested; the schema comes from the
///same <see cref="SchemaVocabulary"/> the editor autocomplete already fetches.
///</summary>
public static class NlQueryPrompt
{
    ///<summary>Human-readable query-language name for the editor language id (gremlin/cypher/sparql).</summary>
    public static string LanguageDisplayName(string language)
    {
        if (language == "cypher")
            return "openCypher";

        if (language == "sparql")
            return "SPARQL";

        return "Gremlin";
    }

    ///<summary>
    ///The system prompt: instruct the model to emit one query in the target language, using only the
    ///schema's labels/keys, with no prose or markdown. Schema sections are omitted when empty, and the
    ///"use only these" rule goes with them — see the three states below.
    ///</summary>
    public static string BuildSystemPrompt(string language, SchemaVocabulary schema, bool toolsAvailable = false)
    {
        var name = LanguageDisplayName(language);
        var sb = new StringBuilder();

        //Three states, not two. A null vocabulary means the schema was never read — disconnected, or the
        //queries failed. A vocabulary that was read but holds nothing means the graph really is empty.
        //Both used to emit "use ONLY the labels listed below" with nothing listed, which constrains the
        //model to the empty set and then tells it to answer anyway — an invitation to invent a schema and
        //sound certain about it.
        bool hasVocabulary = schema != null
            && (schema.VertexLabels is { Count: > 0 }
                || schema.EdgeLabels is { Count: > 0 }
                || schema.PropertyKeys is { Count: > 0 });

        sb.AppendLine($"You generate {name} queries for a graph database.");
        sb.AppendLine($"Given the user's request, output a single {name} query that answers it.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Output ONLY the query. No explanation, no comments, no markdown code fences.");

        if (hasVocabulary)
        {
            sb.AppendLine("- Use ONLY the vertex labels, edge labels and property keys listed below.");
            sb.AppendLine("- If the request cannot be answered from this schema, output the closest valid query.");
        }
        else if (schema != null)
            sb.AppendLine("- The graph is empty — it holds no vertices yet, so there are no labels or property keys to match. Take them from the request itself.");
        else
            sb.AppendLine("- The graph's schema is unknown, so no labels or property keys are given. Take them from the request itself and don't assume any others exist.");

        if (toolsAvailable)
            sb.AppendLine("- You may call run_read_query to run read-only queries against the live graph to inspect the data and verify your query before answering. When you are confident, reply with ONLY the final query and no tool call.");

        if (schema != null)
        {
            if (schema.VertexLabels is { Count: > 0 })
                sb.AppendLine($"\nVertex labels: {string.Join(", ", schema.VertexLabels)}");

            if (schema.EdgeLabels is { Count: > 0 })
                sb.AppendLine($"Edge labels: {string.Join(", ", schema.EdgeLabels)}");

            if (schema.PropertyKeys is { Count: > 0 })
                sb.AppendLine($"Property keys: {string.Join(", ", schema.PropertyKeys)}");
        }

        return sb.ToString().Trim();
    }

    ///<summary>
    ///Strips a surrounding markdown code fence (```lang … ```) and trims, so a fenced reply still drops
    ///cleanly into the editor. The logic lives in <see cref="LlmText.StripMarkdownFences"/>, shared with
    ///the knowledge-graph parser.
    ///</summary>
    public static string CleanQuery(string modelOutput)
    {
        return LlmText.StripMarkdownFences(modelOutput);
    }
}
