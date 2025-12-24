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

        private ArgumentListSyntax WrapArguments(ArgumentListSyntax node)
        {
            var newArguments = SyntaxFactory.SeparatedList(
                node.Arguments.Select((arg, index) =>
                {
                    if (index == 0)
                    {
                        // First argument: no blank line before, just elastic space
                        return arg.WithLeadingTrivia(SyntaxFactory.ElasticSpace)
                                  .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
                    }

                    // Subsequent arguments: line break before
                    return arg.WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                              .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
                }));

            return SyntaxFactory.ArgumentList(newArguments)
                .WithOpenParenToken(
                    node.OpenParenToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
                .WithCloseParenToken(
                    node.CloseParenToken.WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        private ParameterListSyntax WrapParameters(ParameterListSyntax node)
        {
            var newParameters = SyntaxFactory.SeparatedList(
                node.Parameters.Select((p, index) =>
                {
                    if (index == 0)
                    {
                        return p.WithLeadingTrivia(SyntaxFactory.ElasticSpace)
                                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
                    }

                    return p.WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
                }));

            return SyntaxFactory.ParameterList(newParameters)
                .WithOpenParenToken(
                    node.OpenParenToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
                .WithCloseParenToken(
                    node.CloseParenToken.WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
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
                .WithTrailingTrivia(SyntaxFactory.ElasticSpace)
                .WithAdditionalAnnotations(Formatter.Annotation);

            return newExpr;
        }
    }
}
