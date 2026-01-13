using AquaAvgFramework.StoryLineComponents;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using ReciteHelper.Model;
using System.Text.RegularExpressions;

namespace ReciteHelper.Utils;

/// <summary>
/// Provides functionality to execute C# code snippets with restricted types and namespaces for controlled script
/// evaluation.
/// </summary>
/// <remarks>The Parser class is designed to safely evaluate user-provided C# code by limiting accessible types
/// and namespaces. It is intended for scenarios where dynamic code execution is required but must be sandboxed to
/// prevent access to sensitive or unsafe APIs. The class enforces restrictions to help mitigate security risks
/// associated with code injection or unauthorized resource access.</remarks>
public class Parser
{
    public static async Task<string?> ParseConfigText(string? text)
    {
        if (text is null || !text.Contains('%')) return text;

        var code = text.Split('%')[1];
        var result = await ExecuteAsync<string>([typeof(Environment)], ["System"], code);

        // Low-quality handling
        if (result.Result is null) return result.ErrorMessage!;
        else if (result.IsSuccess) return result.Result;
        else throw new InvalidOperationException(result.ErrorMessage);
    }


    public static async Task<StoryLine> CompileStoryAsync(string storyCode)
    {
        //try
        //{
        var options = ScriptOptions.Default
            .WithReferences(typeof(StoryLine).Assembly)
            .AddImports("AquaAvgFramework",
                        "AquaAvgFramework.Animation",
                        "AquaAvgFramework.Animation.Common",
                        "AquaAvgFramework.Animation.Switch",
                        "AquaAvgFramework.GameElements.Blocks",
                        "AquaAvgFramework.GameElements.Events",
                        "AquaAvgFramework.GameElements",
                        "AquaAvgFramework.StoryLineComponents",
                        "AquaAvgFramework.Spirits",
                        "AquaAvgFramework.Global",
                        "AquaAvgFramework.Pools");

        return await CSharpScript.EvaluateAsync<StoryLine>(storyCode, options);
        //}
        //catch (Exception ex)
        //{
        //    Console.WriteLine($"Failed: {ex.Message}");
        //    return null!;
        //}
    }

    private async static Task<ExecutionResult<TOut>> ExecuteAsync<TOut>
        (List<Type> allowedTypes, List<string> allowedNamespaces, string code)
    {
        var _scriptOptions = ScriptOptions.Default
                                          .WithImports(allowedNamespaces.ToArray())
                                          .WithAllowUnsafe(false)
                                          .WithCheckOverflow(true);
        foreach (var type in allowedTypes)
            _scriptOptions = _scriptOptions.AddReferences(type.Assembly);

        try
        {
            // Preprocessing and verification codes
            if (!ValidateCode(code))
            {
                return ExecutionResult<TOut>.Failed("The execution error code contains " +
                    "an operation that is not allowed.");
            }

            // Execute
            var result = await CSharpScript.EvaluateAsync(code, _scriptOptions);
            return ExecutionResult<TOut>.Success((TOut)result);
        }
        catch (Exception ex)
        {
            return ExecutionResult<TOut>.Failed($"Execution error: {ex.Message}");
        }
    }

    private static bool ValidateCode(string code)
    {
        string[] forbiddenPatterns =
        {
            @"File\.", @"Process\.", @"Assembly\.", @"Reflection\.",
            @"Thread\.", @"Task\.", @"Socket\.", @"HttpClient\.",
            @"WebRequest\.", @"SqlConnection\.", @"IO\.", @"Diagnostics\.",
            @"Management\.", @"Registry\.", @"Security\.",
            @"typeof\(", @"Activator\.", @"AppDomain\.", @"Marshal\."
        };

        foreach (var pattern in forbiddenPatterns)
        {
            if (Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase))
                return false;
        }

        return true;
    }
}
