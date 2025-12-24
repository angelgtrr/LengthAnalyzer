using LengthAnalyzer.Test;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LengthAnalyzer.Tests
{
    [TestFixture]
    public class LineLengthAnalyzerTests
    {
        [Test]
        public async Task LongLine_ShouldTriggerDiagnostic()
        {
            var testCode = @"
class C
{
    void M()
    {
        var s = ""This is a very long string that exceeds the maximum line length limit and should be split into smaller strings for readability and maintainability. And here is some extra text to push it over the threshold."";
    }
}";

            var diagnostics = await BaseMethods.GetDiagnosticsAsync(testCode);

            Assert.That(diagnostics.Any(), "Expected a diagnostic for the long line.");
        }

        [Test]
        public async Task WrappedArguments_ShouldNotTriggerDiagnostic()
        {
            var testCode = @"
class C
{
    void M(string a, string b, string c)
    {
        StringAssert.Contains(
            $""{a} {b}"".ToUpper(),
            c,
            ""Header form should contain User firstname and lastname"");
    }
}";

            var diagnostics = await BaseMethods.GetDiagnosticsAsync(testCode);

            Assert.That(diagnostics, Is.Empty, "Expected no diagnostic because arguments are wrapped.");
        }


    }
}
