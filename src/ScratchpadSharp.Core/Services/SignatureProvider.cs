using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace ScratchpadSharp.Core.Services;

public class MethodSignature
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public List<ParameterSignature> Parameters { get; set; } = new();
    public string Documentation { get; set; } = string.Empty;
}

public class ParameterSignature
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Documentation { get; set; } = string.Empty;
    public bool IsParams { get; set; }
}

public interface ISignatureProvider
{
    Task<(List<MethodSignature> Signatures, int ArgumentIndex)> GetSignaturesAsync(
        string code, 
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default);
}

public class SignatureProvider : ISignatureProvider
{
    private readonly AdhocWorkspace workspace;
    private readonly ProjectId projectId;
    private readonly DocumentId documentId;
    private Document currentDocument;

    public SignatureProvider()
    {
        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        workspace = new AdhocWorkspace(host);

        projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "ScratchpadProject",
            assemblyName: "ScratchpadAssembly",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: false),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: MetadataReferenceProvider.GetDefaultReferences());

        workspace.AddProject(projectInfo);

        documentId = DocumentId.CreateNewId(projectId);
        var documentInfo = DocumentInfo.Create(
            documentId,
            name: "Script.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(""), VersionStamp.Create())));

        workspace.AddDocument(documentInfo);
        currentDocument = workspace.CurrentSolution.GetDocument(documentId)!;
    }

    public void UpdateReferences(Dictionary<string, string> nugetPackages)
    {
        var references = MetadataReferenceProvider.GetReferencesWithPackages(nugetPackages);
        var project = workspace.CurrentSolution.GetProject(projectId);
        if (project != null)
        {
            var updatedProject = project.WithMetadataReferences(references);
            workspace.TryApplyChanges(updatedProject.Solution);
        }
    }

    private void UpdateDocument(string code, List<string> usings)
    {
        var usingStatements = string.Join(Environment.NewLine, usings.Select(u => $"using {u};"));
        var fullCode = usingStatements + (usingStatements.Length > 0 ? "\n\n" : "") + code;
        
        System.Diagnostics.Debug.WriteLine($"[SignatureProvider] UpdateDocument - code param length: {code.Length}");
        System.Diagnostics.Debug.WriteLine($"[SignatureProvider] UpdateDocument - code param: '{code}'");
        System.Diagnostics.Debug.WriteLine($"[SignatureProvider] UpdateDocument - usings count: {usings.Count}");
        System.Diagnostics.Debug.WriteLine($"[SignatureProvider] UpdateDocument - fullCode length: {fullCode.Length}");
        System.Diagnostics.Debug.WriteLine($"[SignatureProvider] UpdateDocument - fullCode:\n{fullCode}");
        
        var sourceText = SourceText.From(fullCode);
        
        var solution = workspace.CurrentSolution.WithDocumentText(documentId, sourceText);
        if (workspace.TryApplyChanges(solution))
        {
            currentDocument = workspace.CurrentSolution.GetDocument(documentId)!;
        }
        else
        {
            currentDocument = currentDocument.WithText(sourceText);
        }
    }

    public async Task<(List<MethodSignature> Signatures, int ArgumentIndex)> GetSignaturesAsync(
        string code,
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (nugetPackages?.Count > 0)
            {
                UpdateReferences(nugetPackages);
            }

            UpdateDocument(code, usings);

            var document = workspace.CurrentSolution.GetDocument(documentId);
            if (document == null)
                return (new List<MethodSignature>(), -1);

            var text = await document.GetTextAsync(cancellationToken);
            var fullCode = text.ToString();
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Code length: {code.Length}, Position: {position}");
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Full code length: {fullCode.Length}");
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Full code:\n{fullCode}");
            
            var adjustedPosition = position;
            var usingLinesCount = usings.Count;
            if (usingLinesCount > 0)
            {
                var usingStatements = string.Join(Environment.NewLine, usings.Select(u => $"using {u};"));
                var usingLength = usingStatements.Length + 2;
                adjustedPosition = position + usingLength;
                System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Using length: {usingLength}, Adjusted position: {adjustedPosition}");
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root == null)
                return (new List<MethodSignature>(), -1);

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
                return (new List<MethodSignature>(), -1);

            // Try to find token and walk up the tree
            var token = root.FindToken(adjustedPosition);
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Token: {token}, Parent: {token.Parent?.GetType().Name}");
            
            var invocationNode = FindInvocationOrObjectCreation(token.Parent, adjustedPosition);
            
            // If not found, try to find any invocation/creation near the position
            if (invocationNode == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Trying to find invocation by descendant search...");
                invocationNode = FindInvocationByDescendant(root, adjustedPosition);
            }
            
            if (invocationNode == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SignatureProvider] No invocation found at position {adjustedPosition}");
                return (new List<MethodSignature>(), -1);
            }
            
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Found invocation: {invocationNode.GetType().Name}");

            var symbols = GetInvokedSymbols(invocationNode, semanticModel);
            if (!symbols.Any())
                return (new List<MethodSignature>(), -1);

            var signatures = ExtractSignatures(symbols);
            var argIndex = GetArgumentIndex(invocationNode, adjustedPosition);            
            return (signatures, argIndex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Error: {ex.Message}");
            return (new List<MethodSignature>(), -1);
        }
    }

    private SyntaxNode? FindInvocationOrObjectCreation(SyntaxNode? node, int position)
    {
        int depth = 0;
        while (node != null)
        {
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Checking node depth {depth}: {node.GetType().Name}");
            
            if (node is InvocationExpressionSyntax invocation)
            {
                var openParen = invocation.ArgumentList.OpenParenToken;
                var closeParen = invocation.ArgumentList.CloseParenToken;
                
                // 检查是否在参数列表范围内，或者右括号缺失（正在输入中）
                if (openParen.Span.Start <= position)
                {
                    if (closeParen.IsMissing || position <= closeParen.Span.End)
                    {
                        return invocation;
                    }
                }
            }
            else if (node is ObjectCreationExpressionSyntax objectCreation)
            {
                if (objectCreation.ArgumentList != null)
                {
                    var openParen = objectCreation.ArgumentList.OpenParenToken;
                    var closeParen = objectCreation.ArgumentList.CloseParenToken;
                    
                    if (openParen.Span.Start <= position)
                    {
                        if (closeParen.IsMissing || position <= closeParen.Span.End)
                        {
                            return objectCreation;
                        }
                    }
                }
            }
            
            node = node.Parent;
            depth++;
        }

        System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Reached root after {depth} levels");
        return null;
    }

    private SyntaxNode? FindInvocationByDescendant(SyntaxNode root, int position)
    {
        // Find all invocation and object creation expressions
        var invocations = root.DescendantNodes()
            .Where(n => n is InvocationExpressionSyntax || n is ObjectCreationExpressionSyntax)
            .ToList();
        
        System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Found {invocations.Count} invocation/creation nodes");
        
        foreach (var node in invocations)
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                var openParen = invocation.ArgumentList.OpenParenToken;
                var closeParen = invocation.ArgumentList.CloseParenToken;
                
                System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Invocation '{invocation.Expression}' at {openParen.Span.Start}-{closeParen.Span.End}, missing: {closeParen.IsMissing}");
                
                if (openParen.Span.Start <= position)
                {
                    if (closeParen.IsMissing || position <= closeParen.Span.End)
                    {
                        return invocation;
                    }
                }
            }
            else if (node is ObjectCreationExpressionSyntax objectCreation && objectCreation.ArgumentList != null)
            {
                var openParen = objectCreation.ArgumentList.OpenParenToken;
                var closeParen = objectCreation.ArgumentList.CloseParenToken;
                
                if (openParen.Span.Start <= position)
                {
                    if (closeParen.IsMissing || position <= closeParen.Span.End)
                    {
                        return objectCreation;
                    }
                }
            }
        }
        
        return null;
    }

    private List<ISymbol> GetInvokedSymbols(SyntaxNode node, SemanticModel semanticModel)
    {
        var symbols = new List<ISymbol>();

        if (node is InvocationExpressionSyntax invocation)
        {
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Invocation expression: {invocation.Expression}");
            
            var symbol = semanticModel.GetSymbolInfo(invocation.Expression).Symbol;
            if (symbol != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Found symbol: {symbol.Name}");
                symbols.Add(symbol);
            }

            var methodSymbols = semanticModel.GetMemberGroup(invocation.Expression);
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Member group count: {methodSymbols.Length}");
            symbols.AddRange(methodSymbols.Cast<ISymbol>());
        }
        else if (node is ObjectCreationExpressionSyntax objectCreation)
        {
            var symbol = semanticModel.GetSymbolInfo(objectCreation).Symbol;
            if (symbol != null)
            {
                symbols.Add(symbol);
            }

            var constructorSymbols = semanticModel.GetMemberGroup(objectCreation.Type);
            symbols.AddRange(constructorSymbols.Cast<ISymbol>());
        }

        return symbols.Distinct(SymbolEqualityComparer.Default).ToList();
    }

    private List<MethodSignature> ExtractSignatures(List<ISymbol> symbols)
    {
        var signatures = new List<MethodSignature>();

        foreach (var symbol in symbols)
        {
            if (symbol is IMethodSymbol method)
            {
                signatures.Add(BuildMethodSignature(method));
            }
        }

        return signatures;
    }

    private MethodSignature BuildMethodSignature(IMethodSymbol method)
    {
        var signature = new MethodSignature
        {
            Name = method.Name,
            ReturnType = method.ReturnType.ToDisplayString(),
            Documentation = method.GetDocumentationCommentXml() ?? string.Empty
        };

        foreach (var param in method.Parameters)
        {
            signature.Parameters.Add(new ParameterSignature
            {
                Name = param.Name,
                Type = param.Type.ToDisplayString(),
                IsParams = param.IsParams,
                Documentation = param.GetDocumentationCommentXml() ?? string.Empty
            });
        }

        return signature;
    }

    private int GetArgumentIndex(SyntaxNode node, int position)
    {
        ArgumentListSyntax? argumentList = null;

        if (node is InvocationExpressionSyntax invocation)
        {
            argumentList = invocation.ArgumentList;
        }
        else if (node is ObjectCreationExpressionSyntax objectCreation)
        {
            argumentList = objectCreation.ArgumentList;
        }

        if (argumentList == null)
            return 0;

        if (argumentList.Arguments.Count == 0)
            return 0;

        int argIndex = 0;
        foreach (var arg in argumentList.Arguments)
        {
            if (position < arg.Span.Start)
                break;
            
            if (position <= arg.Span.End)
                return argIndex;
            
            argIndex++;
        }

        return argIndex;
    }

    public void Dispose()
    {
        workspace?.Dispose();
    }
}
