namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using NUnit.Framework;

    public class SharpyBatchTests
    {
        [Test]
        public void ShouldExitWithError_FailedCompileInBatchMode_Exits()
        {
            Assert.IsTrue(SharpyBatch.ShouldExitWithError(true, false));
        }

        [Test]
        public void ShouldExitWithError_SuccessfulCompileInBatchMode_DoesNotExit()
        {
            Assert.IsFalse(SharpyBatch.ShouldExitWithError(true, true));
        }

        [Test]
        public void ShouldExitWithError_Interactive_NeverExits()
        {
            Assert.IsFalse(SharpyBatch.ShouldExitWithError(false, false));
            Assert.IsFalse(SharpyBatch.ShouldExitWithError(false, true));
        }
    }
}
