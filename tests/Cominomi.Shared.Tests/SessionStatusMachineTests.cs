using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class SessionStatusMachineTests
{
    [Theory]
    [InlineData(SessionStatus.Initializing, SessionStatus.Pending, true)]
    [InlineData(SessionStatus.Initializing, SessionStatus.Ready, true)]
    [InlineData(SessionStatus.Initializing, SessionStatus.Error, true)]
    [InlineData(SessionStatus.Initializing, SessionStatus.Archived, false)]
    [InlineData(SessionStatus.Pending, SessionStatus.Initializing, true)]
    [InlineData(SessionStatus.Pending, SessionStatus.Ready, true)]
    [InlineData(SessionStatus.Pending, SessionStatus.Error, true)]
    [InlineData(SessionStatus.Ready, SessionStatus.Error, true)]
    [InlineData(SessionStatus.Ready, SessionStatus.Archived, true)]
    [InlineData(SessionStatus.Error, SessionStatus.Ready, true)]
    [InlineData(SessionStatus.Error, SessionStatus.Initializing, true)]
    [InlineData(SessionStatus.Error, SessionStatus.Archived, true)]
    [InlineData(SessionStatus.Archived, SessionStatus.Ready, false)]
    [InlineData(SessionStatus.Archived, SessionStatus.Initializing, false)]
    public void IsValidTransition_ReturnsExpected(SessionStatus from, SessionStatus to, bool expected)
    {
        Assert.Equal(expected, SessionStatusMachine.IsValidTransition(from, to));
    }

    [Theory]
    [InlineData(SessionStatus.Initializing)]
    [InlineData(SessionStatus.Ready)]
    [InlineData(SessionStatus.Archived)]
    public void IsValidTransition_SameStatus_AlwaysTrue(SessionStatus status)
    {
        Assert.True(SessionStatusMachine.IsValidTransition(status, status));
    }

    [Fact]
    public void Session_TransitionStatus_ValidTransition_ChangesStatus()
    {
        var session = new Session();
        Assert.Equal(SessionStatus.Initializing, session.Status);

        session.TransitionStatus(SessionStatus.Ready);
        Assert.Equal(SessionStatus.Ready, session.Status);
    }

    [Fact]
    public void Session_TransitionStatus_InvalidTransition_Throws()
    {
        var session = new Session();
        session.TransitionStatus(SessionStatus.Ready);
        session.TransitionStatus(SessionStatus.Archived);

        Assert.Throws<InvalidOperationException>(() =>
            session.TransitionStatus(SessionStatus.Initializing));
    }

    [Fact]
    public void Session_TransitionStatus_SameStatus_NoOp()
    {
        var session = new Session();
        session.TransitionStatus(SessionStatus.Ready);
        session.TransitionStatus(SessionStatus.Ready); // should not throw
        Assert.Equal(SessionStatus.Ready, session.Status);
    }

    [Fact]
    public void Session_SetInitialStatus_BypassesValidation()
    {
        var session = new Session();
        session.SetInitialStatus(SessionStatus.Archived);
        Assert.Equal(SessionStatus.Archived, session.Status);
    }

    [Fact]
    public void Session_FullWorkflowPath_Succeeds()
    {
        var session = new Session();
        // Initializing → Ready → Archived
        session.TransitionStatus(SessionStatus.Ready);
        session.TransitionStatus(SessionStatus.Archived);
        Assert.Equal(SessionStatus.Archived, session.Status);
    }

    [Fact]
    public void Session_ErrorRecoveryPath_Succeeds()
    {
        var session = new Session();
        session.TransitionStatus(SessionStatus.Error);
        session.TransitionStatus(SessionStatus.Ready);
        session.TransitionStatus(SessionStatus.Archived);
        Assert.Equal(SessionStatus.Archived, session.Status);
    }
}
