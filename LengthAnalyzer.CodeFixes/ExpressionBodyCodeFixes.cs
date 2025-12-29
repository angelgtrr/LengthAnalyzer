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
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExpressionBodyCodeFixes)), Shared]
    public class ExpressionBodyCodeFixes : CodeFixProvider
    {
        private const string IndentUnit = "    "; // one indent level (4 spaces)

        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(LineLengthAnalyzer.ExpressionBodyTooLongId);

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.FirstOrDefault(d => d.Id == LineLengthAnalyzer.ExpressionBodyTooLongId);
            if (diagnostic == null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Wrap expression body",
                    c => ApplyFixAsync(context.Document, diagnostic, c),
                    "WrapExpressionBody"),
                diagnostic);
        }

        private async Task<Document> ApplyFixAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var spanNode = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            var arrow = spanNode as ArrowExpressionClauseSyntax
                        ?? spanNode.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>();
            var method = arrow?.Parent as MethodDeclarationSyntax;
            if (arrow == null || method == null)
                return document;

            // Always indent continuation line by one level (4 spaces)
            var continuationIndent = IndentUnit;

            // Rewrite => token with newline + indent
            var newArrowToken = SyntaxFactory.Token(
                arrow.ArrowToken.LeadingTrivia,
                arrow.ArrowToken.Kind(),
                new SyntaxTriviaList(
                    SyntaxFactory.EndOfLine("\r\n"),
                    SyntaxFactory.Whitespace(continuationIndent)));

            // Rewrite expression with leading indent
            var newExpr = arrow.Expression.WithLeadingTrivia(
                SyntaxFactory.Whitespace(continuationIndent));

            var newArrow = arrow.WithArrowToken(newArrowToken)
                                .WithExpression(newExpr);

            // Replace the whole method with the new arrow clause
            var newMethod = method.WithExpressionBody(newArrow)
                                  .WithAdditionalAnnotations(Formatter.Annotation);

            var newRoot = root.ReplaceNode(method, newMethod);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
