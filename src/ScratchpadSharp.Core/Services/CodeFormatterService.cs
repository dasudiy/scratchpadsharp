using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

namespace ScratchpadSharp.Core.Services;

public class CodeFormatterService
{
    public async Task<string> FormatCodeAsync(string tabId, string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return sourceCode;

        if (!RoslynWorkspaceService.Instance.IsInitialized)
            return sourceCode;

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = await syntaxTree.GetRootAsync();

            var document = RoslynWorkspaceService.Instance.GetDocument(tabId);
            var workspace = document.Project.Solution.Workspace;

            var formattedNode = Formatter.Format(root, workspace);

            return formattedNode.ToFullString();
        }
        catch
        {
            return sourceCode;
        }
    }
}
