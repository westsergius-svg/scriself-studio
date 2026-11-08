namespace ScriSelfStudio.Core.Tests;

using ScriSelfStudio.Core.Security;

public sealed class SecretProtectorTests
{
    [Fact]
    public void Protected_password_can_be_read_back_and_is_not_plain_text()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        const string password = "app-password-1234";

        var protectedValue = SecretProtector.Protect(password);

        Assert.NotNull(protectedValue);
        Assert.DoesNotContain(password, protectedValue, StringComparison.Ordinal);
        Assert.Equal(password, SecretProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void Broken_protected_value_returns_null()
    {
        Assert.Null(SecretProtector.Unprotect("not-base64"));
    }
}
