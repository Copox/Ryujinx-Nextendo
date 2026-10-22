namespace Ryujinx.HLE.HOS.Services.Nim
{
    enum ResultCode
    {
        ModuleId = 137,
        ErrorCodeShift = 9,

        Success = 0,

        NullArgument = (90 << ErrorCodeShift) | ModuleId,

        // Not a firmware value. nn::ec::detail::ShopServiceAccessServer::ToResult has no mapping for
        // nim description 500, so the SDK passes it to the game unchanged (2137-0500).
        ShopServiceUnavailable = (500 << ErrorCodeShift) | ModuleId,
    }
}
