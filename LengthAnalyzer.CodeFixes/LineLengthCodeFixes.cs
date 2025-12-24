using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace LengthAnalyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LineLengthCodeFixes)), Shared]
    public class LineLengthCodeFixes : CodeFixProvider
    {
        private const string IndentUnit = "    "; // adjust to your team’s indent style

        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(LineLengthAnalyzer.LineTooLongId);

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Wrap long line",
                    createChangedDocument: c => ApplyFixAsync(context.Document, diagnostic, c),
                    equivalenceKey: "WrapLongLine"),
                diagnostic);
        }

        private async Task<Document> ApplyFixAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var spanNode = root.FindNode(diagnostic.Location.SourceSpan);

            // Expand scope: operate on enclosing method if available
            var target = spanNode.FirstAncestorOrSelf<MethodDeclarationSyntax>() ?? spanNode;

            var newTarget = HandleNode(target);
            if (newTarget == null || ReferenceEquals(newTarget, target))
                return document;

            var newRoot = root.ReplaceNode(target, newTarget);
            return document.WithSyntaxRoot(newRoot);
        }

        private SyntaxNode HandleNode(SyntaxNode node)
        {
            switch (node)
            {
                case MethodDeclarationSyntax method:
                    var newParams = WrapParameters(method.ParameterList);

                    var newBody = method.Body != null
                        ? (BlockSyntax)HandleNode(method.Body)
                        : method.Body;

                    var newExprBody = method.ExpressionBody != null
                        ? method.ExpressionBody.WithExpression((ExpressionSyntax)HandleNode(method.ExpressionBody.Expression))
                        : method.ExpressionBody;

                    return method
                        .WithParameterList(newParams)
                        .WithBody(newBody)
                        .WithExpressionBody(newExprBody);

                case BlockSyntax block:
                    return block.ReplaceNodes(block.Statements, (orig, _) => HandleNode(orig));

                case ExpressionStatementSyntax exprStmt:
                    return exprStmt.WithExpression((ExpressionSyntax)HandleNode(exprStmt.Expression));

                case InvocationExpressionSyntax invocation:
                    var newArgs = WrapArguments(invocation.ArgumentList);
                    return invocation.WithArgumentList(newArgs)
                                     .WithAdditionalAnnotations(Formatter.Annotation);

                case ArgumentListSyntax args:
                    return WrapArguments(args);

                case ParameterListSyntax parameters:
                    return WrapParameters(parameters);

                default:
                    return node;
            }
        }

        private static string GetBaseIndent(SyntaxNode node)
        {
            if (node?.SyntaxTree == null)
                return string.Empty;

            var linePos = node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition;
            return new string(' ', linePos.Character);
        }

        // For invocation arguments
        private ArgumentListSyntax WrapArguments(ArgumentListSyntax node)
        {
            // If only one argument, leave it as-is
            if (node.Arguments.Count <= 1)
                return node;

            var baseIndent = GetBaseIndent(node.Parent);
            var argIndent = baseIndent + IndentUnit;

            var args = node.Arguments;
            var items = new System.Collections.Generic.List<SyntaxNodeOrToken>(args.Count * 2);

            var openParen = node.OpenParenToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            for (int i = 0; i < args.Count; i++)
            {
                var argNode = args[i]
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(argIndent))
                    .WithTrailingTrivia(SyntaxTriviaList.Empty);

                items.Add(argNode);

                if (i < args.Count - 1)
                {
                    var comma = SyntaxFactory.Token(SyntaxKind.CommaToken)
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
                    items.Add(comma);
                }
            }

            var separated = SyntaxFactory.SeparatedList<ArgumentSyntax>(items);
            var closeParen = node.CloseParenToken.WithLeadingTrivia(SyntaxTriviaList.Empty);

            return SyntaxFactory.ArgumentList(openParen, separated, closeParen)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        // For method declaration parameters
        private ParameterListSyntax WrapParameters(ParameterListSyntax node)
        {
            // If only one parameter, leave it as-is
            if (node.Parameters.Count <= 1)
                return node;

            var baseIndent = GetBaseIndent(node.Parent);
            var paramIndent = baseIndent + IndentUnit;

            var parameters = node.Parameters;
            var items = new System.Collections.Generic.List<SyntaxNodeOrToken>(parameters.Count * 2);

            var openParen = node.OpenParenToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            for (int i = 0; i < parameters.Count; i++)
            {
                var paramNode = parameters[i]
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(paramIndent))
                    .WithTrailingTrivia(SyntaxTriviaList.Empty);

                items.Add(paramNode);

                if (i < parameters.Count - 1)
                {
                    var comma = SyntaxFactory.Token(SyntaxKind.CommaToken)
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
                    items.Add(comma);
                }
            }

            var separated = SyntaxFactory.SeparatedList<ParameterSyntax>(items);
            var closeParen = node.CloseParenToken.WithLeadingTrivia(SyntaxTriviaList.Empty);

            return SyntaxFactory.ParameterList(openParen, separated, closeParen)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        private SyntaxNode WrapStringLiteral(LiteralExpressionSyntax literal)
        {
            var text = literal.Token.ValueText;
            int splitIndex = text.Length / 2;
            var first = text.Substring(0, splitIndex);
            var second = text.Substring(splitIndex);

            var newExpr = SyntaxFactory.BinaryExpression(
                SyntaxKind.AddExpression,
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(first)),
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(second)))
                .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                .WithTrailingTrivia(SyntaxFactory.Whitespace(GetBaseIndent(literal.Parent) + IndentUnit))
                .WithAdditionalAnnotations(Formatter.Annotation);

            return newExpr;
        }
    }
}
