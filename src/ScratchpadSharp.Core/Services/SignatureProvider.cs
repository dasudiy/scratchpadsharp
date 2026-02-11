using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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
    public string FullSignature { get; set; } = string.Empty;
    public bool IsExtensionMethod { get; set; }
    public string Summary { get; set; } = string.Empty;
    public Dictionary<string, string> ParameterDocs { get; set; } = new();
}

public class ParameterSignature
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Documentation { get; set; } = string.Empty;
    public bool IsParams { get; set; }
    public bool IsOptional { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
}

public interface ISignatureProvider
{
    Task<(List<MethodSignature> Signatures, int ArgumentIndex, int ActiveParameter)> GetSignaturesAsync(
        string tabId,
        string code,
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default);
}

public class SignatureProvider : ISignatureProvider
{
    public async Task<(List<MethodSignature> Signatures, int ArgumentIndex, int ActiveParameter)> GetSignaturesAsync(
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
                return (new List<MethodSignature>(), -1, -1);
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
                return (new List<MethodSignature>(), -1, -1);

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
                return (new List<MethodSignature>(), -1, -1);

            // 查找调用节点
            var invocationContext = FindInvocationContext(root, adjustedPosition);
            if (invocationContext == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SignatureProvider] No invocation context found");
                return (new List<MethodSignature>(), -1, -1);
            }

            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Found invocation: {invocationContext.Node.GetType().Name}");

            var symbols = GetInvokedSymbols(invocationContext.Node, semanticModel);
            if (!symbols.Any())
                return (new List<MethodSignature>(), -1, -1);

            var signatures = ExtractSignatures(symbols);

            // 计算当前参数索引和活动参数
            var (argIndex, activeParam) = CalculateParameterPosition(invocationContext, adjustedPosition);

            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Found {signatures.Count} signatures, arg index: {argIndex}, active param: {activeParam}");

            return (signatures, argIndex, activeParam);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Error: {ex.Message}");
            return (new List<MethodSignature>(), -1, -1);
        }
    }

    private class InvocationContext
    {
        public SyntaxNode Node { get; set; } = null!;
        public ArgumentListSyntax? ArgumentList { get; set; }
        public int OpenParenPosition { get; set; }
        public int CloseParenPosition { get; set; }
        public bool IsComplete { get; set; }
    }

    private static InvocationContext? FindInvocationContext(SyntaxNode root, int position)
    {
        // 首先尝试从当前位置向上查找
        var token = root.FindToken(position);
        var context = FindInvocationFromToken(token.Parent, position);

        if (context != null)
            return context;

        // 如果没找到,尝试在附近的节点中搜索
        return FindNearestInvocation(root, position);
    }

    private static InvocationContext? FindInvocationFromToken(SyntaxNode? node, int position)
    {
        int depth = 0;
        while (node != null && depth < 20)
        {
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Checking node depth {depth}: {node.GetType().Name}");

            if (node is InvocationExpressionSyntax invocation)
            {
                var context = CreateInvocationContext(invocation, invocation.ArgumentList, position);
                if (IsPositionInContext(context, position))
                    return context;
            }
            else if (node is ObjectCreationExpressionSyntax objectCreation && objectCreation.ArgumentList != null)
            {
                var context = CreateInvocationContext(objectCreation, objectCreation.ArgumentList, position);
                if (IsPositionInContext(context, position))
                    return context;
            }
            else if (node is BaseObjectCreationExpressionSyntax baseCreation && baseCreation.ArgumentList != null)
            {
                var context = CreateInvocationContext(baseCreation, baseCreation.ArgumentList, position);
                if (IsPositionInContext(context, position))
                    return context;
            }

            node = node.Parent;
            depth++;
        }

        return null;
    }

    private static InvocationContext? FindNearestInvocation(SyntaxNode root, int position)
    {
        // 查找所有可能的调用表达式
        var candidates = root.DescendantNodes()
            .Where(n => n is InvocationExpressionSyntax ||
                       n is ObjectCreationExpressionSyntax ||
                       n is BaseObjectCreationExpressionSyntax)
            .Select(n =>
            {
                ArgumentListSyntax? argList = n switch
                {
                    InvocationExpressionSyntax inv => inv.ArgumentList,
                    ObjectCreationExpressionSyntax obj => obj.ArgumentList,
                    BaseObjectCreationExpressionSyntax baseObj => baseObj.ArgumentList,
                    _ => null
                };

                if (argList == null) return null;

                return CreateInvocationContext(n, argList, position);
            })
            .Where(c => c != null && IsPositionInContext(c, position))
            .OrderBy(c => Math.Abs(c!.OpenParenPosition - position))
            .FirstOrDefault();

        return candidates;
    }

    private static InvocationContext CreateInvocationContext(
        SyntaxNode node,
        ArgumentListSyntax argumentList,
        int position)
    {
        var openParen = argumentList.OpenParenToken;
        var closeParen = argumentList.CloseParenToken;

        return new InvocationContext
        {
            Node = node,
            ArgumentList = argumentList,
            OpenParenPosition = openParen.SpanStart,
            CloseParenPosition = closeParen.IsMissing ? int.MaxValue : closeParen.SpanStart,
            IsComplete = !closeParen.IsMissing
        };
    }

    private static bool IsPositionInContext(InvocationContext context, int position)
    {
        // 光标必须在开括号之后
        if (position < context.OpenParenPosition)
            return false;

        // 如果有闭括号,光标必须在闭括号之前或之上
        if (context.IsComplete && position > context.CloseParenPosition)
            return false;

        return true;
    }

    private static (int ArgumentIndex, int ActiveParameter) CalculateParameterPosition(
        InvocationContext context,
        int position)
    {
        if (context.ArgumentList == null || context.ArgumentList.Arguments.Count == 0)
        {
            // 空参数列表,但光标在括号内
            return (0, 0);
        }

        int argIndex = 0;
        int activeParam = -1;

        for (int i = 0; i < context.ArgumentList.Arguments.Count; i++)
        {
            var arg = context.ArgumentList.Arguments[i];

            // 检查光标是否在当前参数范围内
            if (position >= arg.SpanStart && position <= arg.Span.End)
            {
                activeParam = i;
                argIndex = i;
                break;
            }

            // 检查光标是否在参数之间(逗号之后)
            if (i < context.ArgumentList.Arguments.Count - 1)
            {
                var nextArg = context.ArgumentList.Arguments[i + 1];
                if (position > arg.Span.End && position < nextArg.SpanStart)
                {
                    // 光标在逗号后,下一个参数前
                    argIndex = i + 1;
                    activeParam = i + 1;
                    break;
                }
            }
        }

        // 如果没有找到活动参数,检查是否在最后一个参数之后
        if (activeParam == -1)
        {
            var lastArg = context.ArgumentList.Arguments.Last();
            if (position > lastArg.Span.End)
            {
                argIndex = context.ArgumentList.Arguments.Count;
                activeParam = context.ArgumentList.Arguments.Count;
            }
        }

        // 如果仍然没有找到,默认使用第一个参数
        if (activeParam == -1)
        {
            argIndex = 0;
            activeParam = 0;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[SignatureProvider] Position {position}, ArgIndex: {argIndex}, ActiveParam: {activeParam}, " +
            $"Args count: {context.ArgumentList.Arguments.Count}");

        return (argIndex, activeParam);
    }

    private static List<ISymbol> GetInvokedSymbols(SyntaxNode node, SemanticModel semanticModel)
    {
        var symbols = new List<ISymbol>();

        if (node is InvocationExpressionSyntax invocation)
        {
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Invocation expression: {invocation.Expression}");

            // 获取方法组中的所有重载
            var methodGroup = semanticModel.GetMemberGroup(invocation.Expression);
            if (methodGroup.Any())
            {
                System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Member group count: {methodGroup.Length}");
                symbols.AddRange(methodGroup.OfType<IMethodSymbol>());
            }

            // 如果没有找到方法组,尝试获取单个符号
            if (!symbols.Any())
            {
                var symbol = semanticModel.GetSymbolInfo(invocation.Expression).Symbol;
                if (symbol is IMethodSymbol)
                {
                    System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Found single symbol: {symbol.Name}");
                    symbols.Add(symbol);
                }
            }
        }
        else if (node is ObjectCreationExpressionSyntax objectCreation)
        {
            var typeInfo = semanticModel.GetTypeInfo(objectCreation.Type);
            if (typeInfo.Type is INamedTypeSymbol namedType)
            {
                // 获取所有构造函数
                symbols.AddRange(namedType.Constructors.Where(c => c.DeclaredAccessibility == Accessibility.Public));
            }
        }
        else if (node is BaseObjectCreationExpressionSyntax baseCreation)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(baseCreation);
            if (symbolInfo.Symbol is IMethodSymbol constructor)
            {
                symbols.Add(constructor);
            }

            // 获取候选符号
            symbols.AddRange(symbolInfo.CandidateSymbols.OfType<IMethodSymbol>());
        }

        return symbols.Distinct(SymbolEqualityComparer.Default).ToList();
    }

    private static List<MethodSignature> ExtractSignatures(List<ISymbol> symbols)
    {
        var signatures = new List<MethodSignature>();

        foreach (var symbol in symbols.OfType<IMethodSymbol>())
        {
            signatures.Add(BuildMethodSignature(symbol));
        }

        // 按参数数量排序,然后按名称排序
        return signatures
            .OrderBy(s => s.Parameters.Count)
            .ThenBy(s => s.Name)
            .ToList();
    }

    private static MethodSignature BuildMethodSignature(IMethodSymbol method)
    {
        var signature = new MethodSignature
        {
            Name = method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name,
            ReturnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(),
            IsExtensionMethod = method.IsExtensionMethod
        };

        // 解析XML文档
        var docComment = method.GetDocumentationCommentXml();
        if (!string.IsNullOrEmpty(docComment))
        {
            ParseDocumentation(docComment, signature);
        }

        // 构建参数列表
        foreach (var param in method.Parameters)
        {
            var paramSig = new ParameterSignature
            {
                Name = param.Name,
                Type = param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                IsParams = param.IsParams,
                IsOptional = param.HasExplicitDefaultValue,
                DefaultValue = param.HasExplicitDefaultValue ? FormatDefaultValue(param.ExplicitDefaultValue) : string.Empty
            };

            // 从文档中获取参数说明
            if (signature.ParameterDocs.TryGetValue(param.Name, out var paramDoc))
            {
                paramSig.Documentation = paramDoc;
            }

            signature.Parameters.Add(paramSig);
        }

        // 构建完整签名字符串
        signature.FullSignature = BuildFullSignatureString(signature);

        return signature;
    }

    private static void ParseDocumentation(string xml, MethodSignature signature)
    {
        try
        {
            var doc = XDocument.Parse(xml);

            // 提取summary
            var summary = doc.Descendants("summary").FirstOrDefault();
            if (summary != null)
            {
                signature.Summary = CleanDocText(summary.Value);
            }

            // 提取参数文档
            foreach (var param in doc.Descendants("param"))
            {
                var paramName = param.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(paramName))
                {
                    signature.ParameterDocs[paramName] = CleanDocText(param.Value);
                }
            }

            // 提取returns
            var returns = doc.Descendants("returns").FirstOrDefault();
            if (returns != null && signature.ReturnType != "void")
            {
                var returnsText = CleanDocText(returns.Value);
                if (!string.IsNullOrEmpty(returnsText))
                {
                    signature.Documentation = signature.Summary + "\n\nReturns: " + returnsText;
                }
                else
                {
                    signature.Documentation = signature.Summary;
                }
            }
            else
            {
                signature.Documentation = signature.Summary;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SignatureProvider] Error parsing XML doc: {ex.Message}");
            signature.Documentation = string.Empty;
        }
    }

    private static string CleanDocText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 移除多余的空白和换行
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(line => line.Trim())
                       .Where(line => !string.IsNullOrEmpty(line));

        return string.Join(" ", lines);
    }

    private static string FormatDefaultValue(object? value)
    {
        if (value == null)
            return "null";

        if (value is string str)
            return $"\"{str}\"";

        if (value is bool b)
            return b ? "true" : "false";

        return value.ToString() ?? string.Empty;
    }

    private static string BuildFullSignatureString(MethodSignature signature)
    {
        var parameters = string.Join(", ", signature.Parameters.Select(p =>
        {
            var parts = new List<string>();

            if (p.IsParams)
                parts.Add("params");

            parts.Add(p.Type);
            parts.Add(p.Name);

            if (p.IsOptional && !string.IsNullOrEmpty(p.DefaultValue))
                parts[parts.Count - 1] += $" = {p.DefaultValue}";

            return string.Join(" ", parts);
        }));

        if (signature.Name == signature.ReturnType) // Constructor
        {
            return $"{signature.Name}({parameters})";
        }
        else
        {
            return $"{signature.ReturnType} {signature.Name}({parameters})";
        }
    }
}
