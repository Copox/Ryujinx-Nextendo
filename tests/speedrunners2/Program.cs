using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Ryujinx.HLE.HOS.Services.Ssl;
using Ryujinx.HLE.HOS.Services.Ssl.SslService;

static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
foreach(var item in new (Exception error,ResultCode expected)[] {
    (new IOException("closed",new SocketException((int)SocketError.ConnectionAborted)),ResultCode.ConnectionAbort),
    (new IOException("reset",new SocketException((int)SocketError.ConnectionReset)),ResultCode.ConnectionReset),
    (new SocketException((int)SocketError.TimedOut),ResultCode.Timeout),
    (new AuthenticationException("invalid handshake"),ResultCode.ConnectionAbort),
}) {
    var restored=0;
    var result=SslHandshakeGuard.Run(()=>throw item.error,()=>restored++);
    Check(result==item.expected && restored==1,"failed handshake did not return the SSL error and restore mode");
}
var successRestore=0;
Check(SslHandshakeGuard.Run(()=>{},()=>successRestore++)==ResultCode.Success && successRestore==1,"success regression");
var failureRestore=0;
try { SslHandshakeGuard.Run(()=>throw new InvalidOperationException("programming error"),()=>failureRestore++);throw new Exception("unexpected exception swallowed"); }
catch(InvalidOperationException) { Check(failureRestore==1,"unexpected failure did not restore mode"); }

// Reproduce the reported failure with a real TLS client and a loopback peer
// that resets the connection without completing its TLS handshake.
var listener=new TcpListener(IPAddress.Loopback,0);
listener.Start();
var port=((IPEndPoint)listener.LocalEndpoint).Port;
var peer=Task.Run(async()=>{
    using var accepted=await listener.AcceptTcpClientAsync();
    accepted.Client.LingerState=new LingerOption(true,0);
});
using var client=new TcpClient();
await client.ConnectAsync(IPAddress.Loopback,port);
using var ssl=new SslStream(client.GetStream(),false);
ssl.ReadTimeout=2000;
ssl.WriteTimeout=2000;
var realRestore=0;
var closedResult=SslHandshakeGuard.Run(()=>ssl.AuthenticateAsClient(new SslClientAuthenticationOptions{TargetHost="localhost"}),()=>realRestore++);
Check((closedResult==ResultCode.ConnectionAbort || closedResult==ResultCode.ConnectionReset) && realRestore==1,"real peer reset escaped the SSL boundary");
await peer.WaitAsync(TimeSpan.FromSeconds(3));
listener.Stop();
Console.WriteLine("PASS: reset/abort/timeout/authentication, restoration, unexpected errors, and actual loopback TLS reset.");

foreach(var host in new[]{"api.braincloudservers.com","ns.photonengine.io","api.gameanalytics.com"}) {
 Check(Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.NextendoSpeedRunners2Redirect.IsServiceHost(host,0x01004CC023582000UL),"SR2 host missing");
 Check(!Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.NextendoSpeedRunners2Redirect.IsServiceHost(host,0x0100FCF002A58000UL),"shared host leaked into UCH rule");
}
Check(!Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.NextendoSpeedRunners2Redirect.IsServiceHost("ns.photonengine.io.example.test",0x01004CC023582000UL),"host suffix accepted");
Check(!Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.NextendoSpeedRunners2Redirect.IsServiceHost(null,0x01004CC023582000UL),"null host accepted");
Console.WriteLine("SR2 title/host isolation passed.");
