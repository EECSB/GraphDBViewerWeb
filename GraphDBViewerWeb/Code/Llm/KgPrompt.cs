using System.Text;

namespace GraphDBViewerWeb.Code;

///<summary>
///Builds the system prompt that turns source text into a strict-JSON knowledge graph. Mirrors
///<see cref="NlQueryPrompt"/>: pure and static, and the schema grounding has three states —
///populated (constrain to the listed labels), read-but-empty (say so) and unknown (drop the
///constraint) — so an empty vocabulary never asserts a constraint over nothing. Must stay
///database-free; the caller passes the vocabulary in.
///</summary>
public static class KgPrompt
{
    ///<summary>
    ///The system prompt. Pass the connected schema's vocabulary to ground labels in it (schema-guided
    ///mode), or null for freeform extraction. The caps quoted to the model are
    ///<see cref="KgGraphParser.MaxNodes"/> / <see cref="KgGraphParser.MaxEdges"/> — the same constants
    ///the parser enforces, so the ask and the enforcement can't drift apart.
    ///</summary>
    public static string BuildSystemPrompt(SchemaVocabulary schema)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You extract a knowledge graph from the user's text.");
        sb.AppendLine("Output a single JSON object of exactly this shape:");
        sb.AppendLine(@"{""nodes"":[{""id"":""acme"",""label"":""Company"",""properties"":{""name"":""Acme""}}],""edges"":[{""source"":""alice"",""target"":""acme"",""label"":""worksAt"",""properties"":{}}]}");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Output ONLY the JSON. No explanation, no comments, no markdown code fences.");
        sb.AppendLine("- Give every node a short lowercase id and a human-readable \"name\" property.");
        sb.AppendLine("- Use canonical entity names: refer to each real-world entity by ONE name everywhere — resolve pronouns, abbreviations and variant spellings to it instead of emitting near-duplicate nodes.");
        sb.AppendLine($"- Extract at most {KgGraphParser.MaxNodes} nodes and {KgGraphParser.MaxEdges} edges; prefer the most important entities and relationships.");

        //Three states, mirroring NlQueryPrompt: a populated vocabulary pins the labels; a read-but-empty
        //graph says so; an unknown schema (freeform, or the schema was never read) just asks for
        //consistent invented labels — never "use only what's listed" with nothing listed.
        bool hasVocabulary = schema != null
            && (schema.VertexLabels is { Count: > 0 }
                || schema.EdgeLabels is { Count: > 0 }
                || schema.PropertyKeys is { Count: > 0 });

        if (hasVocabulary)
        {
            sb.AppendLine("- Use ONLY the vertex labels, edge labels and property keys listed below.");

            if (schema.VertexLabels is { Count: > 0 })
                sb.AppendLine($"\nVertex labels: {string.Join(", ", schema.VertexLabels)}");

            if (schema.EdgeLabels is { Count: > 0 })
                sb.AppendLine($"Edge labels: {string.Join(", ", schema.EdgeLabels)}");

            if (schema.PropertyKeys is { Count: > 0 })
                sb.AppendLine($"Property keys: {string.Join(", ", schema.PropertyKeys)}");
        }
        else if (schema != null)
            sb.AppendLine("- The connected graph is empty — it has no labels or property keys to match. Invent concise, consistent labels from the text itself.");
        else
            sb.AppendLine("- Invent concise, consistent labels from the text itself (e.g. Person, Company, worksAt).");

        return sb.ToString().Trim();
    }
}
