using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace LengthAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LineLengthAnalyzer : DiagnosticAnalyzer
    {
        public const string LineTooLongId = "LINE001";
        public const string ExpressionBodyTooLongId = "LINE002";

        private static readonly DiagnosticDescriptor LineTooLongRule = new DiagnosticDescriptor(
            id: LineTooLongId,
            title: "Line too long",
            messageFormat: "Line exceeds {0} characters",
            category: "Formatting",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ExpressionBodyTooLongRule = new DiagnosticDescriptor(
            id: ExpressionBodyTooLongId,
            title: "Expression body too long",
            messageFormat: "Expression body exceeds {0} characters",
            category: "Formatting",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(LineTooLongRule, ExpressionBodyTooLongRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxTreeAction(AnalyzeTree);
        }

        private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
        {
            var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);
            int maxLength = 140;
            if (options.TryGetValue("max_line_length", out var value) && int.TryParse(value, out var parsed))
                maxLength = parsed;

            var text = context.Tree.GetText(context.CancellationToken);
            var root = context.Tree.GetRoot(context.CancellationToken);

            foreach (var line in text.Lines)
            {
                var lineText = text.ToString(line.Span);
                if (lineText.Length > maxLength)
                {
                    if (lineText.Contains("=>"))
                    {
                        // Try to find the arrow clause on this line
                        var arrow = root.DescendantNodes(line.Span)
                                        .OfType<ArrowExpressionClauseSyntax>()
                                        .FirstOrDefault();

                        if (arrow != null)
                        {
                            // Report diagnostic on the whole arrow clause
                            var diagnostic = Diagnostic.Create(
                                ExpressionBodyTooLongRule,
                                arrow.GetLocation(),
                                maxLength);
                            context.ReportDiagnostic(diagnostic);
                            continue;
                        }

                        // Fallback: if no arrow clause found, still report LINE002 on the line
                        var diagnosticFallback = Diagnostic.Create(
                            ExpressionBodyTooLongRule,
                            Location.Create(context.Tree, line.Span),
                            maxLength);
                        context.ReportDiagnostic(diagnosticFallback);
                    }
                    else
                    {
                        // Generic line too long
                        var diagnostic = Diagnostic.Create(
                            LineTooLongRule,
                            Location.Create(context.Tree, line.Span),
                            maxLength);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }
}
