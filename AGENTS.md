# Release versioning

Use the version properties and comments in `Directory.Build.props`. Publish runs
automatically number RCs; reruns keep the same version. Keep release guidance out
of README.md.

# Commit privacy

Before committing, set `user.email` to `5788605+dombrovsky@users.noreply.github.com`
and verify both `git var GIT_AUTHOR_IDENT` and `git var GIT_COMMITTER_IDENT` use it.
Never include personal email addresses in commit metadata or coauthor trailers.
