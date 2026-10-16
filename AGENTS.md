# Release versioning

Use the version properties and comments in `Directory.Build.props`. Publish runs
automatically number RCs; reruns keep the same version. Keep release guidance out
of README.md.

# Commit privacy

Before committing, set `user.email` to `5788605+dombrovsky@users.noreply.github.com`
and verify both `git var GIT_AUTHOR_IDENT` and `git var GIT_COMMITTER_IDENT` use it.
Never include personal email addresses in commit metadata or coauthor trailers.

# Agent instructions

- Respect the repository's `.editorconfig` for all edits, including indentation, line endings, and whitespace.
- Run `dotnet restore TaskFlow.sln`, then `dotnet format whitespace TaskFlow.sln --no-restore` to fix formatting before completing changes.
- Verify formatting with `dotnet format whitespace TaskFlow.sln --verify-no-changes --no-restore`, matching the main build pipeline.
- Separate groups of private static fields (including static readonly fields), private instance readonly fields, and private mutable instance fields with one empty line. Keep consecutive fields in the same group together. Check this grouping manually; `dotnet format` does not enforce it.