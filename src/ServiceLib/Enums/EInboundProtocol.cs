namespace ServiceLib.Enums;

public enum EInboundProtocol
{
    socks = 0,
    socks2,
    socks3,
    pac,
    api,
    api2,
    mixed,

    /// <summary>Xray's own gRPC API, used to replace the proxy outbound on a server switch.</summary>
    api3,
    speedtest = 21
}
