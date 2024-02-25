namespace RplaceModtools;

public record GithubApiCode(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval);