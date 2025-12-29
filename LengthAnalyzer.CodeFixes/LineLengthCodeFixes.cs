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
        private const string IndentUnit = "    "; // team indent style
        private const int MaxLength = 140;        // analyzer threshold

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
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var spanNode = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            // Prefer constructor initializer wrapping if diagnostic is on a constructor line
            var ctor = spanNode.FirstAncestorOrSelf<ConstructorDeclarationSyntax>()
                       ?? spanNode.Parent?.FirstAncestorOrSelf<ConstructorDeclarationSyntax>();

            if (ctor?.Initializer?.ArgumentList != null)
            {
                var init = ctor.Initializer;
                var colonToken = init.ColonToken;
                var line = text.Lines[text.Lines.GetLineFromPosition(colonToken.SpanStart).LineNumber];
                var lineLength = line.End - line.Start;

                if (lineLength > MaxLength)
                {
                    var wrappedArgs = WrapArgumentsForCtor(init.ArgumentList, ctor);
                    if (!ReferenceEquals(wrappedArgs, init.ArgumentList))
                    {
                        var newCtor = ctor.WithInitializer(init.WithArgumentList(wrappedArgs));
                        var newRoot = root.ReplaceNode(ctor, newCtor.WithAdditionalAnnotations(Formatter.Annotation));
                        return document.WithSyntaxRoot(newRoot);
                    }
                }
            }

            // Fallback path for methods etc.
            var target = spanNode.FirstAncestorOrSelf<MethodDeclarationSyntax>() ?? spanNode;
            var newTarget = HandleNode(target);
            if (newTarget == null || ReferenceEquals(newTarget, target))
                return document;

            var replacedRoot = root.ReplaceNode(target, newTarget);
            return document.WithSyntaxRoot(replacedRoot);
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

        // For general invocation arguments (unchanged)
        private ArgumentListSyntax WrapArguments(ArgumentListSyntax node)
        {
            if (node.Arguments.Count <= 1)
                return node;

            var baseIndent = GetBaseIndent(node.Parent);
            var argIndent = baseIndent + IndentUnit;

            var items = new System.Collections.Generic.List<SyntaxNodeOrToken>(node.Arguments.Count * 2);
            var openParen = node.OpenParenToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                var argNode = node.Arguments[i]
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(argIndent));

                items.Add(argNode);

                if (i < node.Arguments.Count - 1)
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

        // For constructor initializer arguments: each on its own line, continuation indent = constructor indent + one space
        private ArgumentListSyntax WrapArgumentsForCtor(ArgumentListSyntax node, ConstructorDeclarationSyntax ctor)
        {
            if (node.Arguments.Count <= 1)
                return node;

            // Anchor to constructor line start, then add one space for continuation
            var argIndent = GetBaseIndent(ctor) + " ";

            var items = new System.Collections.Generic.List<SyntaxNodeOrToken>(node.Arguments.Count * 2);

            var openParen = node.OpenParenToken
                .WithTrailingTrivia(
                    SyntaxFactory.ElasticCarriageReturnLineFeed,
                    SyntaxFactory.Whitespace(argIndent));

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                var argNode = node.Arguments[i]
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(argIndent));

                items.Add(argNode);

                if (i < node.Arguments.Count - 1)
                {
                    // Comma ends the line, then newline + indent before next argument
                    var comma = SyntaxFactory.Token(SyntaxKind.CommaToken)
                        .WithTrailingTrivia(
                            SyntaxFactory.ElasticCarriageReturnLineFeed,
                            SyntaxFactory.Whitespace(argIndent));
                    items.Add(comma);
                }
            }

            var separated = SyntaxFactory.SeparatedList<ArgumentSyntax>(items);
            var closeParen = node.CloseParenToken.WithLeadingTrivia(SyntaxTriviaList.Empty);

            return SyntaxFactory.ArgumentList(openParen, separated, closeParen)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        // For method/constructor parameters (unchanged)
        private ParameterListSyntax WrapParameters(ParameterListSyntax node)
        {
            if (node.Parameters.Count <= 1)
                return node;

            var baseIndent = GetBaseIndent(node.Parent);
            var paramIndent = baseIndent + IndentUnit;

            var items = new System.Collections.Generic.List<SyntaxNodeOrToken>(node.Parameters.Count * 2);
            var openParen = node.OpenParenToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            for (int i = 0; i < node.Parameters.Count; i++)
            {
                var paramNode = node.Parameters[i]
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(paramIndent));

                items.Add(paramNode);

                if (i < node.Parameters.Count - 1)
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
    }
}
