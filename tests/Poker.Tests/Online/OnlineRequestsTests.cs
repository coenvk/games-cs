using Poker.Online;

namespace Poker.Tests;

/// <summary>The workshop's requests to the server: run off the UI thread one at a time, their outcome taken on it.</summary>
public class OnlineRequestsTests
{
    [Fact]
    public async Task AFinishedRequest_HasItsResultRun_OnTheCallersThread()
    {
        var requests = new OnlineRequests();
        var done = false;

        requests.Start("Asking", () => Task.FromResult<Action>(() => done = true)).Should().BeTrue();
        requests.Waiting.Should().Be("Asking");
        await Until(requests);

        requests.Finish().Should().BeNull();
        done.Should().BeTrue();
        requests.IsBusy.Should().BeFalse();
        requests.Waiting.Should().BeNull();
        requests.Finish().Should().BeNull("nothing is under way");
    }

    [Fact]
    public async Task OnlyOneRequest_RunsAtATime()
    {
        var requests = new OnlineRequests();
        var release = new TaskCompletionSource();
        requests.Start(null, async () =>
        {
            await release.Task;
            return () => { };
        });

        requests.Start(null, () => Task.FromResult<Action>(() => { })).Should().BeFalse();
        requests.Finish().Should().BeNull("the first is still running");
        release.SetResult();
        await Until(requests);
        requests.Finish().Should().BeNull();
    }

    public static TheoryData<string> Failures => ["online problem", "other exception", "throwing result"];

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task EveryFailure_IsHandedBack_NotThrown(string failure)
    {
        var requests = new OnlineRequests();
        Func<Task<Action>> work = failure switch
        {
            "online problem" => () => throw new OnlineProblem("not found", System.Net.HttpStatusCode.NotFound),
            "other exception" => () => Task.FromException<Action>(new ObjectDisposedException("HttpClient")),
            _ => () => Task.FromResult<Action>(() => throw new InvalidOperationException("the result failed")),
        };

        requests.Start("Asking", work);
        await Until(requests);

        var problem = requests.Finish();
        problem.Should().NotBeNull();
        problem.Should().BeOfType(failure switch
        {
            "online problem" => typeof(OnlineProblem),
            "other exception" => typeof(ObjectDisposedException),
            _ => typeof(InvalidOperationException),
        });
        requests.IsBusy.Should().BeFalse("a failed request does not block the next");
    }

    private static async Task Until(OnlineRequests requests)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!requests.IsDone)
        {
            DateTime.UtcNow.Should().BeBefore(deadline);
            await Task.Delay(5);
        }
    }
}
