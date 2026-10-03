using DoViFixer.Application.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;
[TestClass]
public sealed class ApplicationTitleTests
{
    [TestMethod]
    public void NameIsTheAssemblyTitleTheBuildStamped() => Assert.AreEqual("DoViFixer", ApplicationTitle.Name);
}
