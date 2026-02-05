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
        string tabId,
        string code, 
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default);
}

public class SignatureProvider : ISignatureProvider
{
    public async Task<(List<MethodSignature> Signatures, int ArgumentIndex)> GetSignaturesAsync(
        string tabId,
        string code,
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!RoslynWorkspaceService.Instance.IsInitialized)
            {
                System.Diagnostics.Debug.WriteLine("[SignatureProvider] Workspace not initialized yet");
                return (new List<MethodSignature>(), -1);
            }

            if (nugetPackages?.Count > 0)
            {
                await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, nugetPackages);
            }

            await RoslynWorkspaceService.Instance.UpdateDocumentAsync(tabId, code, usings);

            var document = RoslynWorkspaceService.Instance.GetDocument(tabId);

            var adjustedPosition = RoslynWorkspaceService.Instance.CalculateAdjustedPosition(position, usings);

            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Code length: {code.Length}, Position: {position}, Adjusted: {adjustedPosition}");

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

    private static SyntaxNode? FindInvocationOrObjectCreation(SyntaxNode? node, int position)
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

    private static SyntaxNode? FindInvocationByDescendant(SyntaxNode root, int position)
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

    private static List<ISymbol> GetInvokedSymbols(SyntaxNode node, SemanticModel semanticModel)
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

    private static List<MethodSignature> ExtractSignatures(List<ISymbol> symbols)
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

    private static MethodSignature BuildMethodSignature(IMethodSymbol method)
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

    private static int GetArgumentIndex(SyntaxNode node, int position)
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
}
