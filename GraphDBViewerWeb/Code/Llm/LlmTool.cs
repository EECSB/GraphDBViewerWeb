using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GraphDBViewerWeb.Code;

///<summary>A tool the model may call during generation, described in a provider-agnostic way.</summary>
public class LlmTool
{
    public string Name { get; set; }
    public string Description { get; set; }

    ///<summary>JSON Schema (as a JSON string) for the tool's input object, e.g. {"type":"object","properties":{...}}.</summary>
    public string InputSchemaJson { get; set; }
}

///<summary>A tool invocation the model requested — the id is echoed back alongside the result.</summary>
public class LlmToolCall
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string ArgumentsJson { get; set; }
}

///<summary>
///Executes the tools the model calls. Implemented by the app (e.g. running a read-only query against the
///connected database) and passed into a tool-using provider so the LLM code stays free of DB dependencies.
///</summary>
public interface ILlmToolRunner
{
    ///<summary>The tools exposed to the model.</summary>
    IReadOnlyList<LlmTool> Tools { get; }

    ///<summary>Runs a tool by name and returns its result as a string the model reads back.</summary>
    Task<string> RunToolAsync(string name, string argumentsJson, CancellationToken ct);
}

///<summary>An <see cref="ILlmProvider"/> that can drive an agentic tool-use loop. Not all providers implement it.</summary>
public interface IToolUsingLlmProvider
{
    ///<summary>Runs the completion, letting the model call the runner's tools and iterate until it answers.</summary>
    Task<LlmResult> CompleteWithToolsAsync(string systemPrompt, string userPrompt, ILlmToolRunner tools, CancellationToken ct);
}
