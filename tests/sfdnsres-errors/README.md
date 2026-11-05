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

## Full emulator check on macOS

I also built and launched the complete ARM64 GUI emulator before and after
this change, with .NET 10.0.402 on macOS 27.2. I used an isolated portable
profile with my imported firmware 22.5.0. I ran the same original ARM64
homebrew in both apps through File → Load Application. This exercises the
actual guest CPU, service-manager IPC, `sfdnsres`, the system resolver and
the serialized reply read by the guest; it does not call the converter
directly from a host test.

The source in `guest/dns.S` uses no Nintendo SDK, libnx or game code. With
Xcode's LLVM assembler and Python 3, I build it outside the source tree:

```sh
python3 tests/sfdnsres-errors/guest/build.py /tmp/ryujinx-dns-test.nro
```

The guest logs `DNS-GUEST` messages using the ordinary debug-string syscall.
For each case it prints the guest NetDB result, GAI result and serialized
byte count, then a final count of mismatched NetDB/GAI fields:

| Actual emulated request | Original `fa631486` | Fixed `9eb352eb` | Expected |
|---|---|---|---|
| `GetHostByNameRequest("invalid name with spaces")` | `-1 / 14 / 0` | `1 / 7 / 0` | `HostNotFound / NoData / 0` |
| `GetHostByNameRequest("localhost")` | `0 / 0 / 26` | `0 / 0 / 26` | Success and nonempty output |
| `GetAddrInfoRequest("invalid name with spaces")` | `-1 / 1 / 0` | `1 / 7 / 0` | `HostNotFound / NoData / 0` |
| Mismatched result fields | **4** | **0** | **0** |

The localhost byte count is my observed host result, not a portable required
size. Reverse lookup, transient resolver failures and other error mappings
retain the host-fixture coverage above; I did not manufacture those failures
inside the running emulator.

Both full self-contained Release publishes succeeded. They emitted inherited
linker-analysis warnings, so I do not describe them as warning-free. One
fixed-app run separately hit an `ObjectDisposedException` during renderer
shutdown after all guest DNS results had been logged. I am investigating that
lifecycle failure separately; it is not part of the DNS conversion fix. This
check does not establish game compatibility, online connectivity or a speedup.
