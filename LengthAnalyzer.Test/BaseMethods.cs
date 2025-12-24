using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LengthAnalyzer.Test
{
    [TestFixture]
    public class BaseMethods
    {
        public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
        {
            var compilation = await GetDocument(source).Result.Project.GetCompilationAsync();
            var analyzer = new LineLengthAnalyzer();

            var diagnostics = await compilation.WithAnalyzers([analyzer])
                                               .GetAnalyzerDiagnosticsAsync();

            return diagnostics;
        }

        public static async Task<Document> GetDocument(string source)
        {
            var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
                                   .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
            return document;
        }

        public static bool IsDiagnosticPresent(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
        {
            return diagnostics.Any(d => d.Id == diagnosticId);
        }

        public static bool IsFixApplied(string originalSource, string fixedSource)
        {
            return originalSource != fixedSource;
        }

        public static async Task<string> ApplyArgumentsTooLong(string source, Diagnostic diagnostic = null)
        {
            var document = await BaseMethods.GetDocument(source);

            // Run analyzer to get diagnostics if none provided
            if (diagnostic == null)
            {
                var compilation = await document.Project.GetCompilationAsync();
                var analyzer = new LineLengthAnalyzer();
                var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
                                                   .GetAnalyzerDiagnosticsAsync();

                diagnostic = diagnostics.FirstOrDefault();
                if (diagnostic == null)
                    return source; // nothing to fix
            }

            // Apply code fix
            var codeFixProvider = new LineLengthCodeFixes();
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic,
                (a, d) => actions.Add(a), CancellationToken.None);

            await codeFixProvider.RegisterCodeFixesAsync(context);

            if (!actions.Any())
                return source; // no fix available

            var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
            var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
            var newDoc = solution.GetDocument(document.Id);
            var newText = await newDoc.GetTextAsync();

            return newText.ToString();
        }
    }
}
