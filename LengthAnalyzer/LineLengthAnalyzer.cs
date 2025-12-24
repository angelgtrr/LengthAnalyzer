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
        public const string ArgumentsTooLongId = "LINE002";

        private static readonly DiagnosticDescriptor LineTooLongRule = new DiagnosticDescriptor(
            id: LineTooLongId,
            title: "Line too long",
            messageFormat: "Line exceeds {0} characters",
            category: "Formatting",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ArgumentsTooLongRule = new DiagnosticDescriptor(
            id: ArgumentsTooLongId,
            title: "Arguments exceed limit",
            messageFormat: "Function arguments exceed {0} characters, wrap arguments",
            category: "Formatting",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(LineTooLongRule, ArgumentsTooLongRule);

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

            // LINE001: Line-based check
            foreach (var line in text.Lines)
            {
                var lineText = text.ToString(line.Span);
                if (lineText.Length > maxLength)
                {
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
