# Portable resolver error regression

I reproduced the DNS error conversion failure on macOS with .NET 10. The
`SocketException.ErrorCode` value is native to the host: `HostNotFound` was
`-131073`, `TryAgain` was `35`, `NoData` was `96` and `TimedOut` was `60`.
Comparing those values with Winsock integers lost the corresponding guest
resolver errors. `SocketErrorCode` provides the portable enum instead.

I keep the existing guest mappings and unmapped-error fallback. These tests
link the production converter and result enums, construct actual .NET socket
exceptions, and check a malformed-name system lookup. They require no game,
firmware, account or Nextendo server.

```sh
dotnet run --project tests/sfdnsres-errors/Tests.csproj -c Release
```

I recorded 7 failures in 20 checks with the original conversion rules and zero
failures after switching to `SocketErrorCode` on macOS. The CI matrix also
checks Linux and Windows; that does not establish gameplay or online-match
compatibility.
