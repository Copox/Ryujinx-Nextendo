using Ryujinx.HLE.HOS.Services.Sockets.Bsd;
static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
foreach(int timeout in new[]{-1,0,1,100,1000}){
 Require(!BsdPollSchedulingPolicy.ShouldDefer(false,timeout,0x0100152000022000),"MK8 ordinary poll must use 1.9.9 scheduling");
 Require(BsdPollSchedulingPolicy.ShouldDefer(true,timeout,0x0100152000022000),"MK8 eventfd must retain gRPC scheduling");
 foreach(ulong title in new[]{0x0100ABD01785C000UL,0x010012F017576000UL,0x0100000000001000UL}){
  Require(BsdPollSchedulingPolicy.ShouldDefer(true,timeout,title),"Other eventfd polls changed");
  Require(BsdPollSchedulingPolicy.ShouldDefer(false,timeout,title)==(timeout>0),"Other finite-timeout optimization changed");
 }
}
Console.WriteLine("PASS: MK8 ordinary polls restored; eventfd scheduling and Portal 2/GBA/HOME finite polls preserved.");
