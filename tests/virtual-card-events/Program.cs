using System.Reflection;
using System.Runtime.CompilerServices;
using Ryujinx.HLE;

var assembly = typeof(Switch).Assembly;
Type Hle(string name) => assembly.GetType("Ryujinx.HLE." + name, true)!;
object New(string name, params object?[] args) => Activator.CreateInstance(Hle(name), args)!;
object Property(object instance, string name) => instance.GetType().GetProperty(name,
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;
void SetProperty(object instance, string name, object value) => instance.GetType()
    .GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
object Invoke(object instance, string method, params object?[] args) => instance.GetType().GetMethod(method)!.Invoke(instance, args)!;
int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    Console.WriteLine($"PASS {name}");
    checks++;
}

// Build the service's managed object graph without starting emulation or allocating guest RAM.
// Real KEvent, KHandleTable, CMIF registrations and reply descriptors are exercised below.
var device = RuntimeHelpers.GetUninitializedObject(typeof(Switch));
var horizon = RuntimeHelpers.GetUninitializedObject(Hle("HOS.Horizon"));
SetProperty(device, "System", horizon);
var process = RuntimeHelpers.GetUninitializedObject(Hle("HOS.Kernel.Process.KProcess"));
var handles = New("HOS.Kernel.Process.KHandleTable");
Invoke(handles, "Initialize", 32u);
SetProperty(process, "HandleTable", handles);
object Context(int command, out object response, out MemoryStream output)
{
    var requestBytes = new byte[16];
    BitConverter.GetBytes((long)command).CopyTo(requestBytes, 8);
    response = New("HOS.Ipc.IpcMessage");
    output = new MemoryStream();
    return New("HOS.ServiceCtx", device, process, null, null, New("HOS.Ipc.IpcMessage"), response,
        new BinaryReader(new MemoryStream(requestBytes, 0, requestBytes.Length, false, true)), new BinaryWriter(output));
}
var initialContext = Context(0, out _, out _);
var service = New("HOS.Services.Ns.IApplicationManagerInterface", initialContext);
var commands = (IReadOnlyDictionary<int, MethodInfo>)Property(service, "CmifCommands");
var readableType = Hle("HOS.Kernel.Threading.KReadableEvent");
var getObject = handles.GetType().GetMethod("GetObject")!.MakeGenericMethod(readableType);
var registered = new Dictionary<int, int>();
foreach (int command in new[] { 4022, 4088 })
{
    for (int repeat = 0; repeat < 128; repeat++)
    {
        var context = Context(command, out var response, out var output);
        var result = commands[command].Invoke(service, [context]);
        if (result!.ToString() != "Success") throw new Exception($"Command {command} failed: {result}");
        var descriptor = Property(response, "HandleDesc");
        int[] copied = (int[])Property(descriptor, "ToCopy");
        int[] moved = (int[])Property(descriptor, "ToMove");
        if (copied.Length != 1 || copied[0] == 0 || moved.Length != 0 || output.Length != 0)
            throw new Exception($"Invalid CMIF event reply for {command}");
        var readable = getObject.Invoke(handles, [copied[0]])!;
        if (readable == null || (bool)Invoke(readable, "IsSignaled")) throw new Exception("Expected an unsignaled readable event.");
        if (repeat == 0) registered[command] = copied[0];
        if (registered[command] != copied[0]) throw new Exception("Repeated requests allocated another server handle.");

        // Copy to a client table and close the client handle, as IPC copy-handle transfer does.
        var clientHandles = New("HOS.Kernel.Process.KHandleTable");
        Invoke(clientHandles, "Initialize", 4u);
        object?[] copyArgs = [readable, 0];
        clientHandles.GetType().GetMethod("GenerateHandle")!.Invoke(clientHandles, copyArgs);
        int clientHandle = (int)copyArgs[1]!;
        if (!ReferenceEquals(getObject.Invoke(clientHandles, [clientHandle]), readable)) throw new Exception("Copy lost event identity.");
        if (!(bool)Invoke(clientHandles, "CloseHandle", clientHandle)) throw new Exception("Closing client copy failed.");
    }
    Check(true, $"Command {command}: 128 copied readable event replies survive client close/reopen");
}
Check(registered[4022] != registered[4088], "Both notification channels have distinct events");
Check((int)handles.GetType().GetField("_activeSlotsCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handles)! == 2,
    "Repeated requests consume only two server handle slots");
foreach (int command in new[] { 4023, 4053, 4054 })
{
    var context = Context(command, out var response, out var output);
    commands[command].Invoke(service, [context]);
    Check(output.Length == 8 && Property(response, "HandleDesc") == null, $"Scalar command {command} keeps its data reply");
}
Console.WriteLine($"{checks} checks passed.");
