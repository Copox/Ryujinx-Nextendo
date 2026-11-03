# Short guest shutdown regression

I reproduced a renderer-shutdown crash in the complete macOS ARM64 emulator
on current main `9bdbe65a`, with .NET 10.0.402 and macOS 27.2. I used Vulkan
with automatic backend threading and an isolated profile with an empty game
library and my imported firmware 22.5.0.

The original `exit.S` guest only writes `EXIT-GUEST reached` through the
debug-string syscall and calls `ExitProcess`, before presenting a frame.
It contains no Nintendo SDK, libnx, game code or assets. With Xcode's LLVM
assembler and Python 3, I generate the test outside the source tree:

```sh
python3 tests/app-lifecycle/build.py /tmp/ryujinx-exit-test.nro
```

I open the complete emulator app and use File → Load Application to load
that same NRO repeatedly, waiting for the library to return between loads.
On the unmodified main app, one of three guest exits terminated the emulator:

```text
System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'Microsoft.Win32.SafeHandles.SafeWaitHandle'.
   at System.Threading.EventWaitHandle.Set()
   at Ryujinx.Ava.Systems.AppHost.<RenderLoop>b__133_1()
```

`Dispose()` cancels the GPU token. `DisposeContext()` can consequently leave
its `WaitAny` before the render callback finishes. Releasing `_gpuDoneEvent`
at that point lets the callback's `finally` call `Set()` on a disposed event.
I now dispose the renderer and join the owning render thread before releasing
the event and cancellation-token source. The threaded renderer already joins
its GPU worker during disposal; the outer join also covers the direct loop.

I rebuilt the complete app with only that production change and repeated
five loads/exits in the same host process. All five reached the debug-string
message and returned to the library; I observed no unhandled exception or
process restart. Command-Q then closed the host normally. This is a bounded
shutdown regression check, not a gameplay, online or performance result.

The full before/after self-contained Release publishes passed with inherited
linker-analysis warnings. I do not treat the race as occurring on every exit,
or five successful attempts as qualification of every renderer or platform.
