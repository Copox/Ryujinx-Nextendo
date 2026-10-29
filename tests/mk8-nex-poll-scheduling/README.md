# MK8 NEX poll scheduling regression

Run with .NET 10:

```sh
dotnet run --project tests/mk8-nex-poll-scheduling/Test.csproj -c Release
```

The test links the production policy. It checks MK8 ordinary polls, eventfd polls,
and unchanged finite-timeout selection for Portal 2, GBA and HOME over timeouts
-1, 0, 1, 100 and 1000. It does not simulate gameplay or measure performance.

Manual acceptance: on Windows, MK8 Deluxe 4.0.1 with the candidate based on
Ryujinx v1.10.1 reached online modes and allowed gameplay, where the official
v1.10.1 build previously failed with 2306-0502. The tested candidate included
temporary bounded diagnostics, removed from this patch after acceptance.
