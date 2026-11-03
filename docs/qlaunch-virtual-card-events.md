# qlaunch virtual game card notification events

## Failure

On firmware 22.5.0, HOME repeatedly calls ResetSignal with handle zero. The guest treats NS application manager commands 4022 and 4088 as event-returning commands, but the shared GetVirtualGameCardValue handler returned eight zero data bytes and no copy handle.

## ABI evidence

The locally installed qlaunch NSO was examined read-only using LibHac and ARM64 disassembly. Its SHA-256 is `d6aa55744f8b8b5a3f06f7c45b543cae2e029c8b31b8df8569e485c0122aab12`. Offsets below are relative to the uncompressed image:

- Notification setup at `0x9ca5e0` calls `0x528a4`, which imports the event through interface vtable slot `0xb20`.
- The entry at `0x106dff0` points to `0x2d173c`. That stub selects command 4022 and calls the generic copy-handle receiver at `0x5a5c`.
- The second notification setup at `0x9ca63c` calls `0x4f070`, using slot `0xd10` of the same vtable. Its entry at `0x106e1e0` points to `0x2d225c`, which selects command 4088 and uses the same copy-handle receiver.
- The first notification data object is labelled `VgcNotificationData`. Its update method at `0xb60b70` polls both imported events. The observed guest stack includes this update method and the UI traversal above it.

This establishes the handle ABI used by this firmware. The complete notification payloads and notification conditions remain undocumented.

## Change

Commands 4022 and 4088 now return distinct, lazily allocated StubEvent readable copy handles. They remain unsignaled because no notification source is implemented. Commands 4023, 4053 and 4054 retain their scalar reply. Kernel handle validation is unchanged.

## Verification

Run `dotnet run --project tests/virtual-card-events/Tests.csproj -c Release`.

The seven checks use the production CMIF command registrations, handlers, KEvent, KHandleTable and IPC handle descriptors. Emulation and guest RAM are not started. Each event command is requested 128 times; client copies are generated and closed, the original event remains usable, only two server handle slots are occupied, and scalar commands retain their reply shape.

The Windows diagnostic build was exercised in HOME on the user's firmware. Both commands returned real KReadableEvent objects and the repeated ResetSignal InvalidHandle warnings disappeared. Temporary capture code was removed from the final source.

This does not implement the complete virtual game card notification system. No firmware or keys are included in this change.
