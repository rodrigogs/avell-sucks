using System.Collections.Generic;
using AvellSucks.Server.Network;
using Xunit;

namespace AvellSucks.Server.Tests;

public class FirewallManagerTests
{
    private sealed class FakeRunner : ICommandRunner
    {
        public List<string> Calls { get; } = new();
        public int Run(string file, string args, out string output)
        {
            Calls.Add($"{file} {args}");
            output = "";
            return 0;
        }
    }

    [Fact]
    public void OpenPort_deletes_then_adds_inbound_allow_rule()
    {
        var runner = new FakeRunner();
        new FirewallManager(runner).OpenPort(5055);

        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains("delete rule", runner.Calls[0]);
        Assert.Contains("add rule", runner.Calls[1]);
        Assert.Contains("localport=5055", runner.Calls[1]);
        Assert.Contains("dir=in", runner.Calls[1]);
        Assert.Contains(FirewallManager.RuleName, runner.Calls[1]);
    }

    [Fact]
    public void ClosePort_deletes_the_rule()
    {
        var runner = new FakeRunner();
        new FirewallManager(runner).ClosePort(5055);
        Assert.Single(runner.Calls);
        Assert.Contains("delete rule", runner.Calls[0]);
        Assert.Contains(FirewallManager.RuleName, runner.Calls[0]);
    }
}
