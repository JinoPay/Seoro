using Seoro.Shared.Services.Sessions.Native;

namespace Seoro.Shared.Tests;

public class NativeMessageReaderTests
{
    [Theory]
    [InlineData("/Users/parkjinho/Projects/Seoro", "-Users-parkjinho-Projects-Seoro")]
    [InlineData("/Users/me/proj/bin/Debug/net10.0", "-Users-me-proj-bin-Debug-net10-0")]
    [InlineData("/private/tmp", "-private-tmp")]
    public void PathToProjectHash_ReplacesNonAlphaNumericWithDash(string cwd, string expected)
    {
        Assert.Equal(expected, NativeMessageReader.PathToProjectHash(cwd));
    }
}
