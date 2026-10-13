using Gommon;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.Exceptions;
using Ryujinx.HLE.HOS.Ipc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Ryujinx.HLE.HOS.Services
{
    /// <summary>
    /// [Nextendo, diagnostics] The last requests served (service, command, result), always kept:
    /// written by the Diagnostic command, to see what a frozen program keeps asking.
    /// </summary>
    static class IpcRecent
    {
        private const int Size = 256;
        private static readonly string[] _entries = new string[Size];
        private static int _next;

        public static void Record(string service, int command, int result)
        {
            int at = System.Threading.Interlocked.Increment(ref _next) - 1;

            _entries[at & (Size - 1)] = $"{System.DateTime.Now:HH:mm:ss.fff} {service} {command} -> 0x{result:X}";
        }

        public static string Dump()
        {
            System.Text.StringBuilder text = new();
            int next = _next;

            for (int i = System.Math.Max(0, next - Size); i < next; i++)
            {
                text.Append(_entries[i & (Size - 1)]).Append('\n');
            }

            return text.ToString();
        }
    }

    abstract class IpcService
    {
        // [Nextendo, HOME Menu work] NEXTENDO_IPC_TRACE=1 logs every CMIF call and failed result.
        private static readonly bool _ipcTrace = System.Environment.GetEnvironmentVariable("NEXTENDO_IPC_TRACE") == "1";

        public IReadOnlyDictionary<int, MethodInfo> CmifCommands { get; }
        public IReadOnlyDictionary<int, MethodInfo> TipcCommands { get; }

        public ServerBase Server { get; private set; }

        private IpcService _parent;
        private readonly IdDictionary _domainObjects;
        private int _selfId;
        private bool _isDomain;

        // cache array so we don't recreate it all the time
        private object[] _parameters = [null];

        public IpcService(ServerBase server = null, bool registerTipc = false)
        {
            Stopwatch sw = Stopwatch.StartNew();

            CmifCommands = GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                .SelectMany(methodInfo => methodInfo.GetCustomAttributes<CommandCmifAttribute>()
                .Select(command => (command.Id, methodInfo)))
                .ToDictionary(command => command.Id, command => command.methodInfo);

            sw.Stop();

            Logger.Debug?.Print(
                LogClass.Emulation,
                $"{CmifCommands.Count} Cmif commands loaded in {sw.ElapsedTicks} ticks ({Stopwatch.Frequency} tps).",
                GetType().AsPrettyString()
            );

            if (registerTipc)
            {
                sw.Start();

                TipcCommands = GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                    .SelectMany(methodInfo => methodInfo.GetCustomAttributes<CommandTipcAttribute>()
                        .Select(command => (command.Id, methodInfo)))
                    .ToDictionary(command => command.Id, command => command.methodInfo);

                sw.Stop();

                Logger.Debug?.Print(
                    LogClass.Emulation,
                    $"{TipcCommands.Count} Tipc commands loaded in {sw.ElapsedTicks} ticks ({Stopwatch.Frequency} tps).",
                    GetType().AsPrettyString()
                );
            }

            Server = server;

            _parent = this;
            _domainObjects = new IdDictionary();
            _selfId = -1;
        }

        public int ConvertToDomain()
        {
            if (_selfId == -1)
            {
                _selfId = _domainObjects.Add(this);
            }

            _isDomain = true;

            return _selfId;
        }

        public void ConvertToSession()
        {
            _isDomain = false;
        }

        public void CallCmifMethod(ServiceCtx context)
        {
            IpcService service = this;

            if (_isDomain)
            {
                int domainWord0 = context.RequestData.ReadInt32();
                int domainObjId = context.RequestData.ReadInt32();

                int domainCmd = (domainWord0 >> 0) & 0xff;
                int inputObjCount = (domainWord0 >> 8) & 0xff;
                int dataPayloadSize = (domainWord0 >> 16) & 0xffff;

                context.RequestData.BaseStream.Seek(0x10 + dataPayloadSize, SeekOrigin.Begin);

                context.Request.ObjectIds.EnsureCapacity(inputObjCount);

                for (int index = 0; index < inputObjCount; index++)
                {
                    context.Request.ObjectIds.Add(context.RequestData.ReadInt32());
                }

                context.RequestData.BaseStream.Seek(0x10, SeekOrigin.Begin);

                if (domainCmd == 1)
                {
                    service = GetObject(domainObjId);

                    context.ResponseData.Write(0L);
                    context.ResponseData.Write(0L);
                }
                else if (domainCmd == 2)
                {
                    Delete(domainObjId);

                    context.ResponseData.Write(0L);

                    return;
                }
                else
                {
                    throw new NotImplementedException($"Domain command: {domainCmd}");
                }
            }

#pragma warning disable IDE0059 // Remove unnecessary value assignment
            long sfciMagic = context.RequestData.ReadInt64();
#pragma warning restore IDE0059
            int commandId = (int)context.RequestData.ReadInt64();

            bool serviceExists = service.CmifCommands.TryGetValue(commandId, out MethodInfo processRequest);

            if (context.Device.Configuration.IgnoreMissingServices || serviceExists)
            {
                ResultCode result = ResultCode.Success;

                context.ResponseData.BaseStream.Seek(_isDomain ? 0x20 : 0x10, SeekOrigin.Begin);

                if (serviceExists)
                {
                    Logger.Trace?.Print(LogClass.KernelIpc, $"{service.GetType().Name}: {processRequest.Name}");
                    if (_ipcTrace)
                        Logger.Notice.Print(LogClass.KernelIpc, $"[ipc] {service.GetType().Name} {commandId} {processRequest.Name}");

                    _parameters[0] = context;
                    
                    // [Nextendo] A request of a program waits for its answer: one that takes long is a
                    // freeze on screen. Noted, to find them.
                    long asked = Stopwatch.GetTimestamp();

                    result = (ResultCode)processRequest.Invoke(service, _parameters);

                    if (Stopwatch.GetElapsedTime(asked).TotalMilliseconds >= 120)
                    {
                        Logger.Notice.Print(LogClass.KernelIpc, $"[slow] {service.GetType().Name} {commandId} {processRequest.Name}: {Stopwatch.GetElapsedTime(asked).TotalMilliseconds:F0} ms");
                    }

                    IpcRecent.Record(service.GetType().Name, commandId, (int)result);

                    if (_ipcTrace && context.Response.HandleDesc?.ToCopy is { Length: > 0 } copied)
                    {
                        // Which kernel object a returned handle stands for, to match it with a later wait.
                        object given = context.Process.HandleTable.GetObject<Ryujinx.HLE.HOS.Kernel.Common.KAutoObject>(copied[0]);

                        Logger.Notice.Print(LogClass.KernelIpc, $"[handle] {service.GetType().Name} {commandId} -> {given?.GetType().Name}#{(given == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(given)):X}");
                    }
                                        if (_ipcTrace && result != ResultCode.Success)
                    
                        Logger.Notice.Print(LogClass.KernelIpc, $"[ipc]   -> result 0x{(int)result:X} from {service.GetType().Name} {commandId}");
                }
                else
                {
                    string serviceName = (service is not DummyService dummyService) ? service.GetType().FullName : dummyService.ServiceName;

                    Logger.Warning?.Print(LogClass.KernelIpc, $"Missing service {serviceName}: {commandId} ignored");

                    if (_ipcTrace)
                    {
                        // What the request carries tells what the command is: argument bytes left
                        // to read, then the sizes of each kind of buffer.
                        IpcMessage m = context.Request;
                        long args = context.RequestData.BaseStream.Length - context.RequestData.BaseStream.Position;
                        string Sizes<T>(System.Collections.Generic.List<T> l, Func<T, ulong> f) => l.Count == 0 ? "-" : string.Join("/", l.ConvertAll(x => "0x" + f(x).ToString("X")));

                        Logger.Notice.Print(LogClass.KernelIpc, $"[shape] {service.GetType().Name} {commandId}: args={args} in={Sizes(m.SendBuff, x => x.Size)} out={Sizes(m.ReceiveBuff, x => x.Size)} ptr={Sizes(m.PtrBuff, x => x.Size)} recv={Sizes(m.RecvListBuff, x => x.Size)} pid={(m.HandleDesc?.HasPId ?? false)} handles={(m.HandleDesc?.ToCopy.Length ?? 0)} raw={Convert.ToHexString(m.RawData, (int)Math.Min(m.RawData.Length, context.RequestData.BaseStream.Position), (int)Math.Min(40, Math.Max(0, m.RawData.Length - context.RequestData.BaseStream.Position)))}");
                    }

                    // [Nextendo, HOME Menu work] An ignored command used to answer with no data at
                    // all: the caller then read whatever the previous reply left in the buffer as
                    // its output (a count, a size, a flag). Answer zeros instead.
                    context.ResponseData.Write(new byte[0x40]);

                    // A handle handed with an ignored command (a transfer memory, mostly) is let go:
                    // kept, it held the memory it covers, and the caller could not give that memory
                    // back to its heap later (fatal 2001-0106 in the HOME Menu).
                    CloseReceivedHandles(context);
                }

                if (_isDomain)
                {
                    foreach (int id in context.Response.ObjectIds)
                    {
                        context.ResponseData.Write(id);
                    }

                    context.ResponseData.BaseStream.Seek(0, SeekOrigin.Begin);

                    context.ResponseData.Write(context.Response.ObjectIds.Count);
                }

                context.ResponseData.BaseStream.Seek(_isDomain ? 0x10 : 0, SeekOrigin.Begin);

                context.ResponseData.Write(IpcMagic.Sfco);
                context.ResponseData.Write((long)result);
            }
            else
            {
                string dbgMessage = $"{service.GetType().FullName}: {commandId}";

                throw new ServiceNotImplementedException(service, context, dbgMessage);
            }
        }

        public void CallTipcMethod(ServiceCtx context)
        {
            int commandId = (int)context.Request.Type - 0x10;

            bool serviceExists = TipcCommands.TryGetValue(commandId, out MethodInfo processRequest);

            if (context.Device.Configuration.IgnoreMissingServices || serviceExists)
            {
                ResultCode result = ResultCode.Success;

                context.ResponseData.BaseStream.Seek(0x4, SeekOrigin.Begin);

                if (serviceExists)
                {
                    Logger.Debug?.Print(LogClass.KernelIpc, $"{GetType().Name}: {processRequest.Name}");

                    _parameters[0] = context;
                    
                    result = (ResultCode)processRequest.Invoke(this, _parameters);
                }
                else
                {
                    string serviceName;

                    serviceName = (this is not DummyService dummyService) ? GetType().FullName : dummyService.ServiceName;

                    Logger.Warning?.Print(LogClass.KernelIpc, $"Missing service {serviceName}: {commandId} ignored");
                }

                context.ResponseData.BaseStream.Seek(0, SeekOrigin.Begin);

                context.ResponseData.Write((uint)result);
            }
            else
            {
                string dbgMessage = $"{GetType().FullName}: {commandId}";

                throw new ServiceNotImplementedException(this, context, dbgMessage);
            }
        }

        protected void MakeObject(ServiceCtx context, IpcService obj)
        {
            obj.TrySetServer(_parent.Server);

            if (_parent._isDomain)
            {
                obj._parent = _parent;

                context.Response.ObjectIds.Add(_parent.Add(obj));
            }
            else
            {
                context.Device.System.KernelContext.Syscall.CreateSession(out int serverSessionHandle, out int clientSessionHandle, false, 0);

                obj.Server.AddSessionObj(serverSessionHandle, obj);

                // A command may return an event next to the object: keep the handles already set.
                context.Response.HandleDesc = context.Response.HandleDesc?.ToCopy is { Length: > 0 } copies
                    ? new IpcHandleDesc(copies, [clientSessionHandle])
                    : IpcHandleDesc.MakeMove(clientSessionHandle);
            }
        }

        protected T GetObject<T>(ServiceCtx context, int index) where T : IpcService
        {
            int objId = context.Request.ObjectIds[index];

            IpcService obj = _parent.GetObject(objId);

            return obj is T t ? t : null;
        }

        public bool TrySetServer(ServerBase newServer)
        {
            if (Server == null)
            {
                Server = newServer;

                return true;
            }

            return false;
        }

        private int Add(IpcService obj)
        {
            return _domainObjects.Add(obj);
        }

        private bool Delete(int id)
        {
            object obj = _domainObjects.Delete(id);

            if (obj is IDisposable disposableObj)
            {
                disposableObj.Dispose();
            }

            return obj != null;
        }

        private IpcService GetObject(int id)
        {
            return _domainObjects.GetData<IpcService>(id);
        }

        public void SetParent(IpcService parent)
        {
            _parent = parent._parent;
        }

        // [Nextendo] The objects opened through this session, released when the session closes
        // (and only them: DestroyAtExit is for the end of the emulation and may stop servers).
        public void ReleaseDomainObjects()
        {
            foreach (object domainObject in new System.Collections.Generic.List<object>(_domainObjects.Values))
            {
                if (domainObject != this && domainObject is IDisposable disposableObj)
                {
                    disposableObj.Dispose();
                }
            }
        }

        public static void CloseReceivedHandles(ServiceCtx context)
        {
            if (context.Request.HandleDesc == null)
            {
                return;
            }

            foreach (int handle in context.Request.HandleDesc.ToCopy)
            {
                context.Device.System.KernelContext.Syscall.CloseHandle(handle);
            }
        }

        public virtual void DestroyAtExit()
        {
            foreach (object domainObject in _domainObjects.Values)
            {
                if (domainObject != this && domainObject is IDisposable disposableObj)
                {
                    disposableObj.Dispose();
                }
            }

            _domainObjects.Clear();
        }
    }
}
