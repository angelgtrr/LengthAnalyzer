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
using System.Threading;
using System.Threading.Tasks;


namespace LengthAnalyzer.Test
{
    [TestFixture]
    internal class CodeFixesTests
    {
        [Test]
        public async Task CheckAlertTextAndAccept_ShouldBeWrappedByCodeFix()
        {
            var testCode = @"
class C
{
    public void CheckAlertTextAndAccept(AlertMessagesEnum expectedMessage)
    {
        StringAssert.AreEqualIgnoringCase(expectedMessage.ToDescriptionString(), AlertUtils.GetAlertTextAndAccept(), ""Unexpected alert message"");
    }
}";

            var expectedFixedCode = @"
class C
{
    public void CheckAlertTextAndAccept(AlertMessagesEnum expectedMessage)
    {
        StringAssert.AreEqualIgnoringCase(
            expectedMessage.ToDescriptionString(),
            AlertUtils.GetAlertTextAndAccept(),
            ""Unexpected alert message"");
    }
}";

            var diagnostics = await BaseMethods.GetDiagnosticsAsync(testCode);
            Assert.That(diagnostics.Length, Is.GreaterThan(0), "No diagnostics found");

            var newText = await BaseMethods.ApplyArgumentsTooLong(testCode);

            Assert.That(newText.Trim(), Is.EqualTo(expectedFixedCode.Trim()), $"Result: {newText.Trim()} \n expected: {expectedFixedCode}");
        }


        [Test]
        public async Task CheckPopUpTextAndClose_WithTimeout_ShouldBeWrappedByCodeFix()
        {
            var testCode = @"
class C
{
    public void CheckPopUpTextAndClose(BasePopUp popUp1, BasePopUp popUp2, BasePopUp popUp3, PopUpBodyEnum expectedBody, TimeSpan? timeout = null, TimeSpan? timeout2 = null)
    {
    }
}";

            var expectedFixedCode = @"
class C
{
    public void CheckPopUpTextAndClose(
        BasePopUp popUp1,
        BasePopUp popUp2,
        BasePopUp popUp3,
        PopUpBodyEnum expectedBody,
        TimeSpan? timeout = null,
        TimeSpan? timeout2 = null)
    {
    }
}
";

            var diagnostics = await BaseMethods.GetDiagnosticsAsync(testCode);
            Assert.That(diagnostics.Length, Is.GreaterThan(0), "No diagnostics found");
            Assert.That(diagnostics[0].Id, Is.EqualTo("LINE001"), "Expected diagnostic not found");


            var newText = await BaseMethods.ApplyArgumentsTooLong(testCode);

            Assert.That(newText.Trim(), Is.EqualTo(expectedFixedCode.Trim()), $"Result: {newText.Trim()} \n expected: {expectedFixedCode}");
        }

        [Test]
        public async Task CheckPopUpTextAndClose_WithInsertText_ShouldBeWrappedByCodeFix()
        {
            var testCode = @"
class C
{
    public void CheckPopUpTextAndClose(BasePopUp popUp, PopUpBodyEnum expectedBody, string insertText, int insertTextIndex)
    {
        popUp.WaitForFormIsDisplayed();
        StringAssert.AreEqualIgnoringCase(expectedBody.ToDescriptionString().Insert(insertTextIndex, insertText), popUp.GetBody(), ""Unexpected text"");
        popUp.Dismiss();
        popUp.WaitForFormIsNotDisplayed();
    }
}";

            var expectedFixedCode = @"
class C
{
    public void CheckPopUpTextAndClose(
        BasePopUp popUp,
        PopUpBodyEnum expectedBody,
        string insertText,
        int insertTextIndex)
    {
        popUp.WaitForFormIsDisplayed();
        StringAssert.AreEqualIgnoringCase(
            expectedBody.ToDescriptionString().Insert(insertTextIndex, insertText),
            popUp.GetBody(),
            ""Unexpected text"");
        popUp.Dismiss();
        popUp.WaitForFormIsNotDisplayed();
    }
}";

            var newText = await BaseMethods.ApplyArgumentsTooLong(testCode);
            Assert.That(newText.Trim(), Is.EqualTo(expectedFixedCode.Trim()), $"Result: {newText.Trim()} \n expected: {expectedFixedCode}");
        }


        [Test]
        public async Task ConstructorArguments_ShouldBeWrappedByCodeFix()
        {
            var testCode = @"
using Aquality.Appium.Mobile.Elements.Interfaces;
using Aquality.Selenium.Core.Elements;
using IpmQaApp.Screens.Base;
using OpenQA.Selenium;

namespace IpmQaApp.Screens.CintAs
{
    public class CintAsLeftMenuScreen : IpmQaAppLocatorPage
    {
        private ITextBox SearchTxtBx(ElementState elementState = ElementState.Displayed) =>
            ElementFactory.GetTextBox(GetLocator(nameof(SearchTxtBx)), ""Search text box"", state: elementState);

        public CintAsLeftMenuScreen() : base(GetLocatorStatic(typeof(CintAsLeftMenuScreen), nameof(CintAsLeftMenuScreen)), ""CintAs left menu screen"")
        {
        }

        public void Search(string searchRequest)
        {
            var searchTxtBx = SearchTxtBx();
            searchTxtBx.ClearAndType(searchRequest);
            searchTxtBx.SendKeys(Keys.Enter);
        }
    }
}";

            var expectedFixedCode = @"
using Aquality.Appium.Mobile.Elements.Interfaces;
using Aquality.Selenium.Core.Elements;
using IpmQaApp.Screens.Base;
using OpenQA.Selenium;

namespace IpmQaApp.Screens.CintAs
{
    public class CintAsLeftMenuScreen : IpmQaAppLocatorPage
    {
        private ITextBox SearchTxtBx(ElementState elementState = ElementState.Displayed) =>
            ElementFactory.GetTextBox(GetLocator(nameof(SearchTxtBx)), ""Search text box"", state: elementState);

        public CintAsLeftMenuScreen() : base(
                  GetLocatorStatic(typeof(CintAsLeftMenuScreen), nameof(CintAsLeftMenuScreen)),
                  ""CintAs left menu screen"")
        {
        }

        public void Search(string searchRequest)
        {
            var searchTxtBx = SearchTxtBx();
            searchTxtBx.ClearAndType(searchRequest);
            searchTxtBx.SendKeys(Keys.Enter);
        }
    }
}";

            var diagnostics = await BaseMethods.GetDiagnosticsAsync(testCode);
            Assert.That(diagnostics.Length, Is.GreaterThan(0), "No diagnostics found");
            Assert.That(diagnostics[0].Id, Is.EqualTo("LINE001"), "Expected diagnostic not found");

            var newText = await BaseMethods.ApplyArgumentsTooLong(testCode);

            Assert.That(
                newText.Trim(),
                Is.EqualTo(expectedFixedCode.Trim()),
                $"Result: {newText.Trim()} \n expected: {expectedFixedCode}");
        }

        [Test]
        public async Task ExpressionBodyShouldPutMethodOnNewLine()
        {
            var testCode = @"
using System;

public class SupplierHelper
{
    public bool IsApproveOrderLblDisplayed(TimeSpan? timeout = null, TimeSpan? pollingInterval = null) => IsElementDisplayed(ApproveOrderLbl(), timeout, pollingInterval);
}
";

            var expectedFixedCode = @"
using System;

public class SupplierHelper
{
    public bool IsApproveOrderLblDisplayed(TimeSpan? timeout = null, TimeSpan? pollingInterval = null) =>
        IsElementDisplayed(ApproveOrderLbl(), timeout, pollingInterval);
}
";

            var diagnostics = await BaseMethods.GetDiagnosticsAsync(testCode);
            Assert.That(diagnostics.Length, Is.GreaterThan(0), "No diagnostics found");
            Assert.That(diagnostics[0].Id, Is.EqualTo("LINE002"), "Expected diagnostic not found");

            Assert.That(testCode, Contains.Substring("=>"));

            var newText = await BaseMethods.ApplyExpressionBodyTooLong(testCode);

            Assert.That(
                newText.Trim(),
                Is.EqualTo(expectedFixedCode.Trim()),
                $"Result: {newText.Trim()} \n expected: {expectedFixedCode}");
        }

    }
}
