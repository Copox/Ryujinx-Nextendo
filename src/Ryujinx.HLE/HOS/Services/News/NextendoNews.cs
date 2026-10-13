using MsgPack;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.News
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The news of the HOME Menu, taken where a console takes them:
    /// the topic list and the articles Nextendo publishes on its delivery hosts, in the
    /// delivery format (AES-256-CTR under a key derived from the news passphrase).
    ///
    /// On a console a system module downloads them, keeps one row per article in a database
    /// and the article itself as a file; the menu then asks with SQL fragments. This class is
    /// that module's storage: the rows, the files, and just enough of the SQL to answer.
    /// </summary>
    static class NextendoNews
    {
        public const int RecordSize = 0x80;

        private const string Topic = "nx_news_695bb1ea938a4b3e";
        private const string ListHost = "bcat-list-lp1.cdn.nintendo.net";
        private const string HomeMenuTitle = "0100000000001000";

        private sealed class Row
        {
            public readonly Dictionary<string, object> Columns = new(StringComparer.OrdinalIgnoreCase);

            public string Id => (string)Columns["news_id"];

            public long this[string name]
            {
                get => Columns.TryGetValue(name, out object value) && value is long number ? number : 0;
                set => Columns[name] = value;
            }
        }

        private static readonly object _lock = new();
        private static readonly List<Row> _rows = new();
        private static readonly Random _random = new();
        private static int _started;
        private static string _language = "en-US";

        private static string Folder => Path.Combine(AppDataManager.BaseDirPath, "nextendo_news");

        public static string ArticlePath(string newsId) => Path.Combine(Folder, newsId + ".msgpack");

        /// <summary>Starts the first download; later calls cost nothing.</summary>
        public static void Start(string language)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                return;
            }

            if (!string.IsNullOrEmpty(language))
            {
                _language = language;
            }

            Task.Run(() =>
            {
                try
                {
                    LoadKept();
                    Refresh();
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Service, $"[news] Nextendo news unavailable: {e.Message}");
                }
                finally
                {
                    _ready.Set();
                }
            });
        }

        // ------------------------------------------------------------------ download

        private static byte[] Key(out bool found)
        {
            // The passphrase is not in the sources: a release carries it next to the program.
            foreach (string folder in new[] { AppContext.BaseDirectory, AppDataManager.BaseDirPath })
            {
                string file = Path.Combine(folder, "nextendo_news_key.json");

                if (!File.Exists(file))
                {
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));

                string salt = doc.RootElement.GetProperty("sel").GetString();
                string phrase = doc.RootElement.GetProperty("passphrase").GetString();
                string title = doc.RootElement.TryGetProperty("titre_home", out JsonElement t) ? t.GetString() : HomeMenuTitle;

                found = true;

                return Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.ASCII.GetBytes(phrase),
                    Encoding.ASCII.GetBytes(ulong.Parse(title, NumberStyles.HexNumber).ToString("x16") + salt),
                    4096, HashAlgorithmName.SHA256, 32);
            }

            found = false;

            return null;
        }

        private static byte[] Decrypt(byte[] file, byte[] key)
        {
            // "bcat", version, cipher 3 (AES-256-CTR), hash, salt index, 8 empty bytes, the
            // counter block at 0x10, a 0x100-byte signature, then the data.
            if (file.Length < 0x120 || file[0] != 'b' || file[1] != 'c' || file[2] != 'a' || file[3] != 't')
            {
                throw new InvalidDataException("not a delivery file");
            }

            byte[] counter = file.AsSpan(0x10, 0x10).ToArray();
            byte[] clear = new byte[file.Length - 0x120];
            byte[] stream = new byte[16];

            using Aes aes = Aes.Create();

            aes.Key = key;

            for (int at = 0; at < clear.Length; at += 16)
            {
                aes.EncryptEcb(counter, stream, PaddingMode.None);

                for (int k = 0; k < 16 && at + k < clear.Length; k++)
                {
                    clear[at + k] = (byte)(file[0x120 + at + k] ^ stream[k]);
                }

                for (int k = 15; k >= 0 && ++counter[k] == 0; k--)
                {
                }
            }

            return clear;
        }

        private static HttpClient Client()
        {
            // The delivery hosts are Nintendo names that Nextendo answers for: reached at the
            // Nextendo address, the way the console's own name resolution is redirected.
            IPAddress address = Sockets.Sfdnsres.Proxy.DnsMitmResolver.NextendoServerAddress;

            SocketsHttpHandler handler = new()
            {
                ConnectCallback = async (context, cancel) =>
                {
                    Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

                    try
                    {
                        if (address != null)
                        {
                            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancel);
                        }
                        else
                        {
                            await socket.ConnectAsync(context.DnsEndPoint, cancel);
                        }

                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();

                        throw;
                    }
                },
                SslOptions = new SslClientAuthenticationOptions
                {
                    // The certificate is Nextendo's own for those names; what is downloaded is
                    // public and only readable with the news key.
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                },
            };

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        }

        /// <summary>
        /// A file of the delivery service the news page asks for itself (the channel list, a
        /// channel's picture): from Nextendo, deciphered (the page reads it in clear). Null when it
        /// cannot be had.
        /// </summary>
        public static byte[] Download(string url)
        {
            byte[] key = Key(out bool found);

            if (!found || Sockets.Sfdnsres.Proxy.DnsMitmResolver.NextendoServerAddress is null)
            {
                return null;
            }

            // "bcat-topics-%.cdn.nintendo.net": the % is the environment, lp1 for the retail one.
            url = url.Replace("-%.", "-lp1.");

            try
            {
                using HttpClient http = Client();

                return Decrypt(http.GetByteArrayAsync(url).GetAwaiter().GetResult(), key);
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Service, $"[news] {url} not downloaded: {e.Message}");

                return null;
            }
        }

        private static void Refresh()
        {
            byte[] key = Key(out bool found);

            if (!found || Sockets.Sfdnsres.Proxy.DnsMitmResolver.NextendoServerAddress is null || IPAddress.IsLoopback(Sockets.Sfdnsres.Proxy.DnsMitmResolver.NextendoServerAddress))
            {
                Logger.Info?.Print(LogClass.Service, "[news] no news key or no Nextendo address: the news page stays as it is");

                return;
            }

            using HttpClient http = Client();

            byte[] listFile = http.GetByteArrayAsync($"https://{ListHost}/api/nx/v1/list/{Topic}?l={_language}").GetAwaiter().GetResult();
            MessagePackObjectDictionary list = Unpacking.UnpackObject(Decrypt(listFile, key)).Value.AsDictionary();

            Directory.CreateDirectory(Folder);

            int added = 0;

            foreach (MessagePackObject directory in list["directories"].AsList())
            {
                foreach (MessagePackObject entry in directory.AsDictionary()["data_list"].AsList())
                {
                    MessagePackObjectDictionary item = entry.AsDictionary();
                    string newsId = "NS" + item["news_id"].AsInt64().ToString("D20");

                    // The article in the console's language, else in the article's own.
                    string wanted = item.TryGetValue("default_language", out MessagePackObject fallback) ? fallback.AsString() : "en-US";
                    string url = null, any = null;

                    foreach (MessagePackObject choice in item["languages"].AsList())
                    {
                        MessagePackObjectDictionary language = choice.AsDictionary();
                        string code = language["language"].AsString();

                        any ??= language["url"].AsString();

                        if (code == _language)
                        {
                            url = language["url"].AsString();

                            break;
                        }

                        if (code == wanted)
                        {
                            url = language["url"].AsString();
                        }
                    }

                    url ??= any;

                    if (url == null)
                    {
                        continue;
                    }

                    // An article already here is kept, unless the server published it again (its
                    // address changes with its content: new dates, a longer time in Featured).
                    string source = ArticlePath(newsId) + ".source";

                    lock (_lock)
                    {
                        if (_rows.Exists(r => r.Id == newsId) && File.Exists(ArticlePath(newsId)) && File.Exists(source) && File.ReadAllText(source) == url)
                        {
                            continue;
                        }
                    }

                    try
                    {
                        byte[] article = Decrypt(http.GetByteArrayAsync(url).GetAwaiter().GetResult(), key);
                        Row row = MakeRow(newsId, article);

                        File.WriteAllBytes(ArticlePath(newsId), article);
                        File.WriteAllText(source, url);

                        lock (_lock)
                        {
                            _rows.RemoveAll(r => r.Id == newsId);
                            _rows.Add(row);
                        }

                        added++;
                    }
                    catch (Exception e)
                    {
                        Logger.Warning?.Print(LogClass.Service, $"[news] article {newsId} not taken: {e.Message}");
                    }
                }
            }

            Keep();

            Logger.Info?.Print(LogClass.Service, $"[news] Nextendo news: {added} new article(s), {_rows.Count} in all");
        }

        private static long Number(MessagePackObjectDictionary map, string name, long otherwise = 0)
        {
            return map.TryGetValue(name, out MessagePackObject value) && value.IsTypeOf<long>() == true ? value.AsInt64() : otherwise;
        }

        private static Row MakeRow(string newsId, byte[] article)
        {
            MessagePackObjectDictionary map = Unpacking.UnpackObject(article).Value.AsDictionary();
            Row row = new();

            long essential = 0;

            if (map.TryGetValue("essential_pickup", out MessagePackObject pickup) && pickup.IsDictionary)
            {
                essential = Number(pickup.AsDictionary(), "pickup_limit");
            }

            row.Columns["news_id"] = newsId;
            row.Columns["user_id"] = "";
            row.Columns["topic_id"] = map.TryGetValue("topic_id", out MessagePackObject topic) ? topic.AsString() : Topic;
            row.Columns["application_ids"] = "";
            row["received_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            row["published_at"] = Number(map, "published_at");
            row["expire_at"] = Number(map, "expire_at");
            row["pickup_limit"] = Number(map, "pickup_limit");
            row["essential_pickup_limit"] = essential;
            // An article put forward ("essential_pickup") is kept by the console with priority 1000
            // (its own database: the two rows with such a limit have 1000, the others 100 or 50):
            // the lock screen's Featured News asks for 1000 to 1999, still within the pickup limit.
            row["priority"] = essential > 0 ? Math.Max(1000, Number(map, "priority", 100)) : Number(map, "priority", 100);
            row["deletion_priority"] = Number(map, "deletion_priority", 100);
            row["surprise"] = Number(map, "surprise");
            row["decoration_type"] = Number(map, "decoration_type");
            row["newly"] = 1;
            row["opted_in"] = 1;

            foreach (string name in new[] { "age_limit", "point", "read", "displayed", "point_status", "feedback", "reputation", "extra_1", "extra_2" })
            {
                row[name] = 0;
            }

            return row;
        }

        // ------------------------------------------------------------------ what is kept

        private static void Keep()
        {
            try
            {
                List<Dictionary<string, object>> all = new();

                lock (_lock)
                {
                    foreach (Row row in _rows)
                    {
                        all.Add(new Dictionary<string, object>(row.Columns));
                    }
                }

                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, "news.json"), JsonSerializer.Serialize(all));
            }
            catch (Exception)
            {
                // kept next time
            }
        }

        private static void LoadKept()
        {
            string file = Path.Combine(Folder, "news.json");

            if (!File.Exists(file))
            {
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));

            lock (_lock)
            {
                foreach (JsonElement entry in doc.RootElement.EnumerateArray())
                {
                    Row row = new();

                    foreach (JsonProperty column in entry.EnumerateObject())
                    {
                        row.Columns[column.Name] = column.Value.ValueKind == JsonValueKind.Number ? column.Value.GetInt64() : column.Value.GetString() ?? "";
                    }

                    if (row.Columns.ContainsKey("news_id") && File.Exists(ArticlePath(row.Id)) && !_rows.Exists(r => r.Id == row.Id))
                    {
                        // rows kept from before articles put forward had priority 1000 (see MakeRow)
                        if (row["essential_pickup_limit"] > 0 && row["priority"] < 1000)
                        {
                            row["priority"] = 1000;
                        }

                        _rows.Add(row);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the menu's questions

        // The first download, done or given up: the menu's first questions wait for it a little
        // (the lock screen asks for the news as soon as it shows, and asks only once).
        private static readonly ManualResetEventSlim _ready = new(false);

        private static void WaitFirstDownload()
        {
            if (Volatile.Read(ref _started) != 0 && !_ready.IsSet)
            {
                _ready.Wait(5000);
            }
        }

        public static int Count(string where)
        {
            WaitFirstDownload();

            lock (_lock)
            {
                return Select(where).Count;
            }
        }

        public static void Update(string key, int value, bool add, string where)
        {
            lock (_lock)
            {
                foreach (Row row in Select(where))
                {
                    row[key] = add ? row[key] + value : value;
                }
            }

            Keep();
        }

        public static byte[] List(string where, string order, int offset, int capacity, out int count)
        {
            WaitFirstDownload();

            lock (_lock)
            {
                List<Row> rows = Select(where);

                Sort(rows, order);

                count = Math.Max(0, Math.Min(capacity, rows.Count - offset));

                byte[] records = new byte[count * RecordSize];

                for (int i = 0; i < count; i++)
                {
                    Row row = rows[offset + i];
                    Span<byte> record = records.AsSpan(i * RecordSize, RecordSize);

                    Encoding.ASCII.GetBytes(row.Id).AsSpan().CopyTo(record);
                    Encoding.ASCII.GetBytes((string)row.Columns["user_id"]).AsSpan().CopyTo(record[0x18..]);
                    Encoding.ASCII.GetBytes((string)row.Columns["topic_id"]).AsSpan().CopyTo(record[0x30..]);
                    BitConverter.TryWriteBytes(record[0x50..], row["received_at"]);
                    BitConverter.TryWriteBytes(record[0x60..], (int)row["decoration_type"]);
                    BitConverter.TryWriteBytes(record[0x64..], (int)row["read"]);
                    BitConverter.TryWriteBytes(record[0x68..], (int)row["newly"]);
                    BitConverter.TryWriteBytes(record[0x6C..], (int)row["displayed"]);
                    BitConverter.TryWriteBytes(record[0x70..], (int)row["feedback"]);
                    BitConverter.TryWriteBytes(record[0x78..], (int)row["extra_1"]);
                    BitConverter.TryWriteBytes(record[0x7C..], (int)row["extra_2"]);
                }

                return records;
            }
        }

        private static List<Row> Select(string where)
        {
            List<Row> chosen = new();

            foreach (Row row in _rows)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(where) || Truth(new Sql(where, row).Expression()))
                    {
                        chosen.Add(row);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Service, $"[news] phrase not understood \"{where}\": {e.Message}");

                    break;
                }
            }

            return chosen;
        }

        private static void Sort(List<Row> rows, string order)
        {
            if (string.IsNullOrWhiteSpace(order) || rows.Count < 2)
            {
                return;
            }

            List<(string Text, bool Descending)> terms = new();

            foreach (string part in SplitTopLevel(order))
            {
                string text = part.Trim();
                bool descending = false;

                if (text.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase))
                {
                    descending = true;
                    text = text[..^5];
                }
                else if (text.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase))
                {
                    text = text[..^4];
                }

                terms.Add((text, descending));
            }

            // Each key is worked out once per row (one of them is random).
            Dictionary<Row, object[]> keys = new();

            try
            {
                foreach (Row row in rows)
                {
                    object[] values = new object[terms.Count];

                    for (int t = 0; t < terms.Count; t++)
                    {
                        values[t] = new Sql(terms[t].Text, row).Expression();
                    }

                    keys[row] = values;
                }
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Service, $"[news] order not understood \"{order}\": {e.Message}");

                return;
            }

            rows.Sort((a, b) =>
            {
                for (int t = 0; t < terms.Count; t++)
                {
                    int c = Compare(keys[a][t], keys[b][t]);

                    if (c != 0)
                    {
                        return terms[t].Descending ? -c : c;
                    }
                }

                return 0;
            });
        }

        private static IEnumerable<string> SplitTopLevel(string text)
        {
            int depth = 0, start = 0;
            bool quoted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '\'')
                {
                    quoted = !quoted;
                }
                else if (!quoted && c == '(')
                {
                    depth++;
                }
                else if (!quoted && c == ')')
                {
                    depth--;
                }
                else if (!quoted && depth == 0 && c == ',')
                {
                    yield return text[start..i];

                    start = i + 1;
                }
            }

            yield return text[start..];
        }

        private static bool Truth(object value) => value is long number ? number != 0 : value is string text && text.Length > 0;

        private static int Compare(object a, object b)
        {
            if (a is long x && b is long y)
            {
                return x.CompareTo(y);
            }

            return string.CompareOrdinal(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The part of SQL the menu writes: comparisons, AND/OR/NOT, arithmetic, columns,
        /// numbers, quoted text, and the module's own functions N_IF, N_SWITCH and N_RAND.
        /// </summary>
        private sealed class Sql
        {
            private readonly string _text;
            private readonly Row _row;
            private int _at;

            public Sql(string text, Row row)
            {
                _text = text;
                _row = row;
            }

            public object Expression()
            {
                object value = Or();

                Skip();

                if (_at < _text.Length)
                {
                    throw new FormatException($"unexpected '{_text[_at]}' at {_at}");
                }

                return value;
            }

            private void Skip()
            {
                while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                {
                    _at++;
                }
            }

            private bool Word(string word)
            {
                Skip();

                if (_at + word.Length <= _text.Length &&
                    string.Compare(_text, _at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                    (_at + word.Length == _text.Length || !(char.IsLetterOrDigit(_text[_at + word.Length]) || _text[_at + word.Length] == '_')))
                {
                    _at += word.Length;

                    return true;
                }

                return false;
            }

            private bool Sign(string sign)
            {
                Skip();

                if (string.CompareOrdinal(_text, _at, sign, 0, sign.Length) == 0)
                {
                    _at += sign.Length;

                    return true;
                }

                return false;
            }

            private object Or()
            {
                object left = And();

                while (Word("OR"))
                {
                    object right = And();

                    left = Truth(left) || Truth(right) ? 1L : 0L;
                }

                return left;
            }

            private object And()
            {
                object left = Not();

                while (Word("AND"))
                {
                    object right = Not();

                    left = Truth(left) && Truth(right) ? 1L : 0L;
                }

                return left;
            }

            private object Not()
            {
                if (Word("NOT"))
                {
                    return Truth(Not()) ? 0L : 1L;
                }

                return Comparison();
            }

            private object Comparison()
            {
                object left = Sum();

                foreach (string sign in new[] { "!=", "<>", ">=", "<=", "==", "=", ">", "<" })
                {
                    if (Sign(sign))
                    {
                        int c = Compare(left, Sum());

                        return sign switch
                        {
                            "!=" or "<>" => c != 0,
                            ">=" => c >= 0,
                            "<=" => c <= 0,
                            ">" => c > 0,
                            "<" => c < 0,
                            _ => c == 0,
                        } ? 1L : 0L;
                    }
                }

                return left;
            }

            private object Sum()
            {
                object left = Product();

                while (true)
                {
                    if (Sign("+"))
                    {
                        left = Long(left) + Long(Product());
                    }
                    else if (Sign("-"))
                    {
                        left = Long(left) - Long(Product());
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            private object Product()
            {
                object left = Unary();

                while (true)
                {
                    if (Sign("*"))
                    {
                        left = Long(left) * Long(Unary());
                    }
                    else if (Sign("/"))
                    {
                        long divisor = Long(Unary());

                        left = divisor == 0 ? 0L : Long(left) / divisor;
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            private static long Long(object value) => value is long number ? number : long.TryParse(value as string, out long parsed) ? parsed : 0;

            private object Unary()
            {
                if (Sign("-"))
                {
                    return -Long(Unary());
                }

                return Primary();
            }

            private object Primary()
            {
                Skip();

                if (_at >= _text.Length)
                {
                    throw new FormatException("phrase ends early");
                }

                char c = _text[_at];

                if (c == '(')
                {
                    _at++;

                    object value = Or();

                    if (!Sign(")"))
                    {
                        throw new FormatException("missing ')'");
                    }

                    return value;
                }

                if (c == '\'')
                {
                    int end = _text.IndexOf('\'', _at + 1);

                    if (end < 0)
                    {
                        throw new FormatException("text not closed");
                    }

                    string text = _text[(_at + 1)..end];

                    _at = end + 1;

                    return text;
                }

                if (char.IsDigit(c))
                {
                    int start = _at;

                    while (_at < _text.Length && char.IsDigit(_text[_at]))
                    {
                        _at++;
                    }

                    return long.Parse(_text[start.._at], CultureInfo.InvariantCulture);
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = _at;

                    while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] == '_'))
                    {
                        _at++;
                    }

                    string name = _text[start.._at];

                    if (!Sign("("))
                    {
                        return _row.Columns.TryGetValue(name, out object column) ? column : 0L;
                    }

                    List<object> arguments = new();

                    if (!Sign(")"))
                    {
                        do
                        {
                            arguments.Add(Or());
                        }
                        while (Sign(","));

                        if (!Sign(")"))
                        {
                            throw new FormatException("missing ')' after arguments");
                        }
                    }

                    return Call(name.ToUpperInvariant(), arguments);
                }

                throw new FormatException($"unexpected '{c}' at {_at}");
            }

            private static object Call(string name, List<object> arguments)
            {
                switch (name)
                {
                    case "N_IF" when arguments.Count == 3:
                        return Truth(arguments[0]) ? arguments[1] : arguments[2];
                    case "N_SWITCH" when arguments.Count >= 2:
                        // N_SWITCH(value, case, result, case, result..., otherwise)
                        for (int i = 1; i + 1 < arguments.Count; i += 2)
                        {
                            if (Compare(arguments[0], arguments[i]) == 0)
                            {
                                return arguments[i + 1];
                            }
                        }

                        return arguments.Count % 2 == 0 ? arguments[^1] : 0L;
                    case "N_RAND" when arguments.Count == 2:
                        lock (_random)
                        {
                            return _random.NextInt64(Long(arguments[0]), Math.Max(Long(arguments[0]) + 1, Long(arguments[1])));
                        }
                    default:
                        throw new FormatException($"unknown function {name}/{arguments.Count}");
                }
            }
        }
    }
}
