using DoViFixer.Application.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;
[TestClass]
public sealed class ApplicationVersionTests
{
    [TestMethod]
    [DataRow("0.1.0", "0.1.0")]
    [DataRow("0.1.0-dev", "0.1.0-dev")]
    [DataRow("0.1.0-dev+abc1234", "0.1.0-dev+abc1234")]
    [DataRow("1.2.3+0123456789abcdef0123456789abcdef01234567", "1.2.3+0123456")]
    [DataRow("0.1.0-ci.42+0123456789abcdef0123456789abcdef01234567", "0.1.0-ci.42+0123456")]
    public void FormatShortensTheCommitHash(string informationalVersion, string expected) => Assert.AreEqual(expected, ApplicationVersion.Format(informationalVersion));

    [TestMethod]
    public void OfReadsTheVersionTheBuildStamped() => StringAssert.Matches(ApplicationVersion.Of(typeof(ApplicationVersion).Assembly), new System.Text.RegularExpressions.Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?(\+[0-9a-f]{7})?$"));
}
