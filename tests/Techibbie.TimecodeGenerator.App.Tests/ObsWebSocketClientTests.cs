using System;
using System.Security.Cryptography;
using System.Text;
using Techibbie.TimecodeGenerator.App.Services;
using Xunit;

namespace Techibbie.TimecodeGenerator.App.Tests;

public class ObsWebSocketClientTests
{
    // Independently computes the same obs-websocket v5 auth formula the production code
    // implements (base64(sha256(base64(sha256(password+salt)) + challenge)))), using plain
    // SHA256 calls right here in the test rather than calling back into the code under test —
    // this is what actually proves ComputeAuthResponse implements the documented spec, not
    // just that it's internally consistent with itself.
    private static string ExpectedAuthResponse(string password, string salt, string challenge)
    {
        using var sha256 = SHA256.Create();
        var secretBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + salt));
        var base64Secret = Convert.ToBase64String(secretBytes);
        var responseBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(base64Secret + challenge));
        return Convert.ToBase64String(responseBytes);
    }

    [Theory]
    [InlineData("", "somesalt", "somechallenge")]
    [InlineData("hunter2", "aXNhbHRlZA==", "Y2hhbGxlbmdl")]
    [InlineData("correct horse battery staple", "", "")]
    public void ComputeAuthResponse_MatchesTheDocumentedFormula(string password, string salt, string challenge)
    {
        var expected = ExpectedAuthResponse(password, salt, challenge);

        var actual = ObsWebSocketClient.ComputeAuthResponse(password, salt, challenge);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeAuthResponse_DifferentPasswordsProduceDifferentResponses()
    {
        var a = ObsWebSocketClient.ComputeAuthResponse("password-one", "salt", "challenge");
        var b = ObsWebSocketClient.ComputeAuthResponse("password-two", "salt", "challenge");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeAuthResponse_DifferentSaltsProduceDifferentResponses()
    {
        var a = ObsWebSocketClient.ComputeAuthResponse("password", "salt-one", "challenge");
        var b = ObsWebSocketClient.ComputeAuthResponse("password", "salt-two", "challenge");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeAuthResponse_DifferentChallengesProduceDifferentResponses()
    {
        var a = ObsWebSocketClient.ComputeAuthResponse("password", "salt", "challenge-one");
        var b = ObsWebSocketClient.ComputeAuthResponse("password", "salt", "challenge-two");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeAuthResponse_IsDeterministic()
    {
        var a = ObsWebSocketClient.ComputeAuthResponse("password", "salt", "challenge");
        var b = ObsWebSocketClient.ComputeAuthResponse("password", "salt", "challenge");

        Assert.Equal(a, b);
    }
}
