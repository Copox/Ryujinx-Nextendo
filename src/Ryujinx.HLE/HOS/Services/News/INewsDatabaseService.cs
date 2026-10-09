using Ryujinx.Common.Logging;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.News
{
    // [Nextendo, HOME Menu work] The news database: the menu asks it with SQL fragments
    // (a "where" phrase, an "order by" phrase) over the table of received news.
    class INewsDatabaseService : IpcService
    {
        private static string Text(ServiceCtx context, int index)
        {
            if (index >= context.Request.PtrBuff.Count)
            {
                return "";
            }

            ulong position = context.Request.PtrBuff[index].Position;
            ulong size = context.Request.PtrBuff[index].Size;

            if (size == 0)
            {
                return "";
            }

            byte[] raw = new byte[size];

            context.Memory.Read(position, raw);

            int end = System.Array.IndexOf(raw, (byte)0);

            return Encoding.UTF8.GetString(raw, 0, end < 0 ? raw.Length : end);
        }

        [CommandCmif(1)]
        // Count(buffer<string, 9> where) -> u32
        public ResultCode Count(ServiceCtx context)
        {
            string where = Text(context, 0);
            int count = NextendoNews.Count(where);

            Logger.Notice.Print(LogClass.Service, $"[news] Count where \"{where}\" -> {count}");

            context.ResponseData.Write(count);

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // CountWithKey(buffer<string, 9> key, buffer<string, 9> where) -> u32
        public ResultCode CountWithKey(ServiceCtx context)
        {
            string key = Text(context, 0);
            string where = Text(context, 1);
            int count = NextendoNews.Count(where);

            Logger.Notice.Print(LogClass.Service, $"[news] CountWithKey \"{key}\" where \"{where}\" -> {count}");

            context.ResponseData.Write(count);

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // UpdateIntegerValue(u32 value, buffer<string, 9> key, buffer<string, 9> where)
        public ResultCode UpdateIntegerValue(ServiceCtx context)
        {
            int value = context.RequestData.ReadInt32();
            string key = Text(context, 0);
            string where = Text(context, 1);

            Logger.Notice.Print(LogClass.Service, $"[news] Update {key} = {value} where \"{where}\"");

            NextendoNews.Update(key, value, false, where);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // UpdateIntegerValueWithAddition(u32 value, buffer<string, 9> key, buffer<string, 9> where)
        public ResultCode UpdateIntegerValueWithAddition(ServiceCtx context)
        {
            int value = context.RequestData.ReadInt32();
            string key = Text(context, 0);
            string where = Text(context, 1);

            Logger.Notice.Print(LogClass.Service, $"[news] Update {key} += {value} where \"{where}\"");

            NextendoNews.Update(key, value, true, where);

            return ResultCode.Success;
        }

        [CommandCmif(1000)] // 6.0.0+
        // GetList(u32 offset, buffer<string, 9> where, buffer<string, 9> order) -> (u32 count, buffer<NewsRecord, 6>)
        public ResultCode GetList(ServiceCtx context)
        {
            int offset = context.RequestData.ReadInt32();
            string where = Text(context, 0);
            string order = Text(context, 1);

            ulong position = context.Request.ReceiveBuff[0].Position;
            int capacity = (int)(context.Request.ReceiveBuff[0].Size / NextendoNews.RecordSize);

            byte[] records = NextendoNews.List(where, order, offset, capacity, out int count);

            if (records.Length > 0)
            {
                context.Memory.Write(position, records);
            }

            Logger.Notice.Print(LogClass.Service, $"[news] GetList where \"{where}\" order \"{order}\" offset {offset} room {capacity} -> {count}");

            context.ResponseData.Write(count);

            return ResultCode.Success;
        }
    }
}
