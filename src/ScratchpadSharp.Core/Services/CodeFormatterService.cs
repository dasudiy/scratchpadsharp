using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;

namespace ScratchpadSharp.Core.Services;

public class CodeFormatterService
{
    public async Task<string> FormatCodeAsync(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return sourceCode;

        using var workspace = new AdhocWorkspace(MefHostServices.Create(MefHostServices.DefaultAssemblies));
        
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = await syntaxTree.GetRootAsync();

        var formattedNode = Formatter.Format(root, workspace);

        return formattedNode.ToFullString();
    }
}
