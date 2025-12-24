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
        private const string IndentUnit = "    "; // one extra indent level (4 spaces). Adjust to your team's settings.

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
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            var newNode = HandleNode(node);

            if (newNode == null)
                return document;

            var newRoot = root.ReplaceNode(node, newNode);
            return document.WithSyntaxRoot(newRoot);
        }

        private SyntaxNode HandleNode(SyntaxNode node)
        {
            switch (node)
            {
                case ArgumentListSyntax args:
                    return WrapArguments(args);

                case ParameterListSyntax parameters:
                    return WrapParameters(parameters);

                case LiteralExpressionSyntax literal
                    when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    return WrapStringLiteral(literal);

                case InvocationExpressionSyntax invocation:
                    return invocation
                        .WithArgumentList(WrapArguments(invocation.ArgumentList))
                        .WithAdditionalAnnotations(Formatter.Annotation);

                case ExpressionStatementSyntax exprStmt:
                    return exprStmt.WithExpression(
                        (ExpressionSyntax)HandleNode(exprStmt.Expression));

                case MethodDeclarationSyntax methodDecl:
                    return methodDecl.WithParameterList(
                        (ParameterListSyntax)HandleNode(methodDecl.ParameterList));

                default:
                    // Recurse into children
                    return node.ReplaceNodes(
                        node.ChildNodes(),
                        (original, _) => HandleNode(original));
            }
        }

        private static string GetBaseIndent(SyntaxNode node)
        {
            if (node?.SyntaxTree == null)
                return string.Empty;

            var linePos = node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition;
            return new string(' ', linePos.Character);
        }

        private ArgumentListSyntax WrapArguments(ArgumentListSyntax node)
        {
            var baseIndent = GetBaseIndent(node.Parent);     // align ')' with invocation
            var argIndent = baseIndent + IndentUnit;         // indent arguments one level deeper

            var args = node.Arguments;
            var items = new System.Collections.Generic.List<SyntaxNodeOrToken>(args.Count * 2);

            // After '(' put a newline; the first argument will add the indent.
            var openParen = node.OpenParenToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            for (int i = 0; i < args.Count; i++)
            {
                var arg = args[i];

                // Each argument line starts with indent only
                var argNode = arg
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(argIndent))
                    .WithTrailingTrivia(SyntaxTriviaList.Empty);

                items.Add(argNode);

                if (i < args.Count - 1)
                {
                    // Comma: newline only (no indent here)
                    var comma = SyntaxFactory.Token(SyntaxKind.CommaToken)
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

                    items.Add(comma);
                }
            }

            var separated = SyntaxFactory.SeparatedList<ArgumentSyntax>(items);

            // Before ')' put a newline + base indent, aligning the closing paren with the invocation
            var closeParen = node.CloseParenToken.WithLeadingTrivia(
                SyntaxFactory.ElasticCarriageReturnLineFeed,
                SyntaxFactory.Whitespace(baseIndent));

            return SyntaxFactory.ArgumentList(openParen, separated, closeParen)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        private ParameterListSyntax WrapParameters(ParameterListSyntax node)
        {
            var baseIndent = GetBaseIndent(node.Parent);
            var paramIndent = baseIndent + IndentUnit;

            var parameters = node.Parameters;
            var items = new System.Collections.Generic.List<SyntaxNodeOrToken>(parameters.Count * 2);

            var openParen = node.OpenParenToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];

                var paramNode = p
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(paramIndent))
                    .WithTrailingTrivia(SyntaxTriviaList.Empty);

                items.Add(paramNode);

                if (i < parameters.Count - 1)
                {
                    var comma = SyntaxFactory.Token(SyntaxKind.CommaToken)
                        .WithTrailingTrivia(
                            SyntaxFactory.ElasticCarriageReturnLineFeed,
                            SyntaxFactory.Whitespace(paramIndent));

                    items.Add(comma);
                }
            }

            var separated = SyntaxFactory.SeparatedList<ParameterSyntax>(items);

            var closeParen = node.CloseParenToken.WithLeadingTrivia(
                SyntaxFactory.ElasticCarriageReturnLineFeed,
                SyntaxFactory.Whitespace(baseIndent));

            return SyntaxFactory.ParameterList(openParen, separated, closeParen)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        private SyntaxNode WrapStringLiteral(LiteralExpressionSyntax literal)
        {
            var text = literal.Token.ValueText;

            // naive split at midpoint for demo purposes
            int splitIndex = text.Length / 2;
            var first = text.Substring(0, splitIndex);
            var second = text.Substring(splitIndex);

            var newExpr = SyntaxFactory.BinaryExpression(
                SyntaxKind.AddExpression,
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(first)),
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(second)))
                .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                .WithTrailingTrivia(SyntaxFactory.Whitespace(GetBaseIndent(literal.Parent) + IndentUnit))
                .WithAdditionalAnnotations(Formatter.Annotation);

            return newExpr;
        }
    }
}
