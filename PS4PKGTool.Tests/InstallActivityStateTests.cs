using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Tests;

[TestClass]
public class InstallActivityStateTests
{
    [TestMethod]
    public void LegalTransitions_AreAccepted()
    {
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Idle, InstallActivityState.Preparing));

        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Preparing, InstallActivityState.Running));
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Preparing, InstallActivityState.Cancelled)); // pre-first-stage cancel
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Preparing, InstallActivityState.Failed));

        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Running, InstallActivityState.Cancelling));
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Running, InstallActivityState.Completed));
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Running, InstallActivityState.Cancelled));
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Running, InstallActivityState.Failed));

        // Late cancel that crossed the non-cancellable commit boundary: the
        // real result (Completed) wins over the requested cancel.
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelling, InstallActivityState.Cancelled));
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelling, InstallActivityState.Completed));
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelling, InstallActivityState.Failed));

        // Terminal states can only be dismissed.
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Completed, InstallActivityState.Idle));
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelled, InstallActivityState.Idle));
        Assert.IsTrue(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Failed, InstallActivityState.Idle));
    }

    [TestMethod]
    public void IllegalTransitions_AreRejected()
    {
        // Idle only starts.
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Idle, InstallActivityState.Running));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Idle, InstallActivityState.Completed));

        // Preparing cannot be re-entered or jumped forward.
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Preparing, InstallActivityState.Preparing));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Preparing, InstallActivityState.Cancelling));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Preparing, InstallActivityState.Completed));

        // Running cannot go back or restart.
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Running, InstallActivityState.Running));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Running, InstallActivityState.Preparing));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Running, InstallActivityState.Idle));

        // Cancelling cannot resume.
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelling, InstallActivityState.Running));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelling, InstallActivityState.Cancelling));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelling, InstallActivityState.Preparing));

        // Terminal states never restart or re-run.
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Completed, InstallActivityState.Running));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelled, InstallActivityState.Running));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Failed, InstallActivityState.Preparing));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Completed, InstallActivityState.Completed));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Cancelled, InstallActivityState.Cancelled));
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition(InstallActivityState.Failed, InstallActivityState.Failed));

        // Unknown/zero default is rejected as a source.
        Assert.IsFalse(InstallActivityStateMachine.IsValidTransition((InstallActivityState)999, InstallActivityState.Idle));
    }
}
