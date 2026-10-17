using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Ryujinx.Tests.HLE
{
    /// <summary>
    /// Ce que GetFriendList rend a Super Smash Bros. Ultimate (NextendoFriends.GetForSmashLaunch),
    /// dans l'ordre reel d'un lancement : InitializePresence lit la liste (OnGameLaunched), le premier
    /// GetFriendList arrive plus tard (8,7 s apres « Application Loaded » dans un journal du 17/10), le
    /// second 0,6 s apres lui. Le serveur de comptes est un HttpListener local, le compte et ses amis
    /// sont des comptes d'essai (PID 100 a 103).
    /// </summary>
    [NonParallelizable]
    public class SmashFriendListTests
    {
        private const string Smash = NextendoFriends.SmashUltimateTitleId;

        // Comme FriendService.SmashListWaitMs.
        private const int SmashListWaitMs = 8000;

        private static readonly byte[] _threeFriends = Encoding.UTF8.GetBytes(
            "{\"friends\":[{\"pid\":101,\"name\":\"a\"},{\"pid\":102,\"name\":\"b\"},{\"pid\":103,\"name\":\"c\"}]}");

        private readonly record struct Reply(int Status, int DelayMs, byte[] Body);

        private static Reply Ok(int delayMs = 0) => new(200, delayMs, _threeFriends);

        private static Reply NoFriends() => new(200, 0, Encoding.UTF8.GetBytes("{\"friends\":[]}"));

        private static Reply Down(int delayMs = 0) => new(503, delayMs, Encoding.UTF8.GetBytes("indisponible"));

        private readonly Stopwatch _sinceLaunch = new();
        private HttpListener _server;
        private string _dir;
        private string _previousApi;

        // Reponse a la n-ieme requete /api/friends du test (1, 2...).
        private volatile Func<int, Reply> _friends = _ => Down();
        private int _friendRequests;

        [OneTimeSetUp]
        public void StartAccountServer()
        {
            TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            string url = $"http://localhost:{port}";
            _previousApi = Environment.GetEnvironmentVariable("NEXTENDO_API");
            Environment.SetEnvironmentVariable("NEXTENDO_API", url);

            // Jamais le vrai serveur : l'adresse de l'API est figee a sa premiere lecture.
            Assert.That(NextendoEndpoint.BaseUrl(), Is.EqualTo(url), "adresse de l'API deja lue avant ces tests");

            _dir = Path.Combine(Path.GetTempPath(), "ryujinx-tests-smash-amis-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "nextendo_account.txt"), "pid=100\nusername=essai\nnex_token=jeton-essai\n");
            AppDataManager.Initialize(_dir);

            // Jamais le vrai compte non plus.
            Assert.That(NextendoAccount.IsLinked && NextendoAccount.Pid == 100, "compte d'essai non charge");

            _server = new HttpListener();
            _server.Prefixes.Add(url + "/");
            _server.Start();
            new Thread(Serve) { IsBackground = true }.Start();
        }

        [OneTimeTearDown]
        public void StopAccountServer()
        {
            WaitForReadsToEnd();
            _server?.Close();
            Environment.SetEnvironmentVariable("NEXTENDO_API", _previousApi);

            try
            {
                Directory.Delete(_dir, true);
            }
            catch
            {
                // un fichier encore ouvert : le dossier temporaire reste
            }
        }

        [SetUp]
        public void Reset()
        {
            WaitForReadsToEnd();
            _friends = _ => Down();
            NextendoFriends.Invalidate();
            Interlocked.Exchange(ref _friendRequests, 0);
        }

        [TearDown]
        public void LetReadsEnd() => WaitForReadsToEnd();

        private static void WaitForReadsToEnd()
        {
            Stopwatch sw = Stopwatch.StartNew();

            while (NextendoFriends.IsRefreshing && sw.ElapsedMilliseconds < 15000)
            {
                Thread.Sleep(20);
            }
        }

        private void Serve()
        {
            while (_server.IsListening)
            {
                HttpListenerContext c;

                try
                {
                    c = _server.GetContext();
                }
                catch
                {
                    return;
                }

                ThreadPool.QueueUserWorkItem(_ => Answer(c));
            }
        }

        private void Answer(HttpListenerContext c)
        {
            try
            {
                // /api/history et /api/me (lus en tache de fond par chaque lecture) : rien.
                Reply r = c.Request.Url.AbsolutePath == "/api/friends"
                    ? _friends(Interlocked.Increment(ref _friendRequests))
                    : new Reply(404, 0, []);

                Thread.Sleep(r.DelayMs);
                c.Response.StatusCode = r.Status;
                c.Response.OutputStream.Write(r.Body);
                c.Response.Close();
            }
            catch
            {
                // le client a abandonne
            }
        }

        // Lancement de Smash : InitializePresence remet l'attente et lit la liste.
        private void Launch(Func<int, Reply> friends)
        {
            _friends = friends;
            _sinceLaunch.Restart();
            NextendoFriends.OnGameLaunched(Smash);
        }

        // Un GetFriendList de Smash, comme FriendService le sert.
        private static (int Count, long Ms) GetFriendList(int waitMs = SmashListWaitMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int count = NextendoFriends.GetForSmashLaunch(waitMs).Count;

            return (count, sw.ElapsedMilliseconds);
        }

        [TestCase("01006a800016e000", true)]
        [TestCase("01006A800016E000", true)]
        [TestCase("0100152000022000", false)] // Mario Kart 8 Deluxe : liste interrogee en boucle, jamais bloquee
        [TestCase("01003bc0000a0000", false)] // Splatoon 2
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OnlySmashUltimateWaitsForItsFriendListAtLaunch(string titleId, bool expected)
        {
            Assert.That(NextendoFriends.ReadsListOnlyOnLaunch(titleId), Is.EqualTo(expected));
        }

        // Echec puis succes : la lecture du lancement rate, le serveur revient avant le premier
        // GetFriendList, moins de 10 s apres l'echec. C'est la regression de 0e96ecba3 : 0 ami.
        [Test]
        public void FailedLaunchReadThenServerBack_FirstCallGetsTheList()
        {
            Launch(_ => _sinceLaunch.ElapsedMilliseconds < 1500 ? Down() : Ok());
            Thread.Sleep(2500);

            (int count, long ms) = GetFriendList();
            Assert.That(count, Is.EqualTo(3));
            Assert.That(ms, Is.LessThan(1000));

            Thread.Sleep(600);
            (count, ms) = GetFriendList();
            Assert.That(count, Is.EqualTo(3));
            Assert.That(ms, Is.LessThan(100));

            Assert.That(_friendRequests, Is.EqualTo(2));
        }

        // Echec puis succes pendant l'attente : le serveur revient alors que le premier
        // GetFriendList attend deja.
        [Test]
        public void ServerBackDuringTheWait_FirstCallGetsTheList()
        {
            Launch(_ => _sinceLaunch.ElapsedMilliseconds < 1500 ? Down() : Ok());
            Thread.Sleep(600);

            (int count, long ms) = GetFriendList();
            Assert.That(count, Is.EqualTo(3));
            Assert.That(ms, Is.LessThan(SmashListWaitMs));

            (count, ms) = GetFriendList();
            Assert.That(count, Is.EqualTo(3));
            Assert.That(ms, Is.LessThan(100));

            Assert.That(_friendRequests, Is.LessThanOrEqualTo(3));
        }

        // Echec lent puis succes : la lecture du lancement, encore en cours au premier
        // GetFriendList, finit par rater ; la suivante repond.
        [Test]
        public void LaunchReadFailingWhileWaited_FirstCallRetriesAndGetsTheList()
        {
            Launch(n => n == 1 ? Down(1500) : Ok());
            Thread.Sleep(300);

            (int count, long ms) = GetFriendList();
            Assert.That(count, Is.EqualTo(3));
            Assert.That(ms, Is.LessThan(SmashListWaitMs));

            Assert.That(_friendRequests, Is.EqualTo(2));
        }

        // Succes lent : le premier GetFriendList attend la lecture en cours, et elle seule.
        [Test]
        public void SlowServer_FirstCallWaitsForTheReadInProgress()
        {
            Launch(_ => Ok(2500));
            Thread.Sleep(600);

            (int count, long ms) = GetFriendList();
            Assert.That(count, Is.EqualTo(3));
            Assert.That(ms, Is.InRange(1000, SmashListWaitMs - 1));

            (count, ms) = GetFriendList();
            Assert.That(count, Is.EqualTo(3));
            Assert.That(ms, Is.LessThan(100));

            Assert.That(_friendRequests, Is.EqualTo(1));
        }

        // Echec durable : une seule attente bornee, le second GetFriendList ne bloque pas, et un jeu
        // NEX qui interroge en boucle ne relance pas /api/friends (une lecture par 10 s).
        [Test]
        public void ServerDown_OneBoundedWaitAndNoLoop()
        {
            Launch(_ => Down());
            Thread.Sleep(600);

            (int count, long ms) = GetFriendList();
            Assert.That(count, Is.Zero);
            Assert.That(ms, Is.LessThanOrEqualTo(SmashListWaitMs + 500));

            Thread.Sleep(600);
            (count, ms) = GetFriendList();
            Assert.That(count, Is.Zero);
            Assert.That(ms, Is.LessThan(100));

            for (int i = 0; i < 40; i++)
            {
                NextendoFriends.Get();
                Thread.Sleep(50);
            }

            // la lecture du lancement, puis 2 au plus pendant l'attente (1 s, puis 3 s d'ecart)
            Assert.That(_friendRequests, Is.LessThanOrEqualTo(3));
        }

        // Echec durable, serveur qui ne repond pas : l'attente s'arrete a son delai.
        [Test]
        public void ServerHanging_TheWaitIsBounded()
        {
            Launch(_ => Down(5000));
            Thread.Sleep(300);

            (int count, long ms) = GetFriendList(2000);
            Assert.That(count, Is.Zero);
            Assert.That(ms, Is.InRange(1900, 2600));

            (count, ms) = GetFriendList(2000);
            Assert.That(count, Is.Zero);
            Assert.That(ms, Is.LessThan(100));

            Assert.That(_friendRequests, Is.EqualTo(1));
        }

        // Chaque lancement a droit a sa propre attente, meme si la derniere lecture ratee date de
        // moins de 10 s.
        [Test]
        public void EachLaunchGetsItsOwnWait()
        {
            Launch(_ => Down());

            (int count, long ms) = GetFriendList(1500);
            Assert.That(count, Is.Zero);
            Assert.That(GetFriendList(1500).Ms, Is.LessThan(100));

            Launch(_ => Ok());

            (count, ms) = GetFriendList();
            Assert.That(count, Is.EqualTo(3));
            Assert.That(ms, Is.LessThan(2000));
        }

        // Un compte sans ami : la liste lue (vide) rend la main, sans attendre le delai.
        [Test]
        public void AccountWithoutFriends_NoWaitBeyondTheRead()
        {
            Launch(_ => NoFriends());

            (int count, long ms) = GetFriendList();
            Assert.That(count, Is.Zero);
            Assert.That(ms, Is.LessThan(1000));
            Assert.That(NextendoFriends.HasLoaded, Is.True);

            Assert.That(_friendRequests, Is.EqualTo(1));
        }
    }
}
