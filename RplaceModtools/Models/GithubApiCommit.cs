namespace  RplaceModtools.Models;

public record GithubApiCommitItem(string Sha, GithubApiCommit Commit);
// Useless github encapsulation
public record GithubApiCommit(GithubApiCommitter Committer);
public record GithubApiCommitter(string Date);
