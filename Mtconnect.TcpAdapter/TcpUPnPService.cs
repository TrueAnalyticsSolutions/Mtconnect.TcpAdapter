using Mtconnect.AdapterSdk;
using Mtconnect.AdapterSdk.UPnP;

namespace Mtconnect.UPnP
{
    /// <inheritdoc />
    public sealed class TcpUPnPService : UPnPService<TcpAdapter>
    {
        /// <inheritdoc cref="UPnPService{T}.UPnPService(T, double, string, int, IAdapterLogger)"/>
        /// <param name="adapter">Reference to a <see cref="TcpAdapter"/> instance.</param>
        /// <param name="broadcastRate"><inheritdoc cref="UPnPService{T}.UPnPService(T, double, string, int, IAdapterLogger)" path="[@name='broadcastRate']"/></param>
        /// <param name="address"><inheritdoc cref="UPnPService{T}.UPnPService(T, double, string, int, IAdapterLogger)" path="[@name='address']"/></param>
        /// <param name="port"><inheritdoc cref="UPnPService{T}.UPnPService(T, double, string, int, IAdapterLogger)" path="[@name='port']"/></param>
        /// <param name="logger"><inheritdoc cref="UPnPService{T}.UPnPService(T, double, string, int, IAdapterLogger)" path="[@name='logFactory']"/></param>
        public TcpUPnPService(TcpAdapter adapter, double broadcastRate = DEFAULT_BROADCAST_RATE, string address = DEFAULT_UPnP_ADDRESS, int port = DEFAULT_UPnP_PORT, IAdapterLogger logger = default) : base(adapter, broadcastRate, address, port, logger) { }

        /// <inheritdoc />
        protected override UPnPDeviceServiceModel ConstructDescription()
        {
            var model = base.ConstructDescription();
            model.AdapterEndpoint = ConvertLocalhost(Adapter.Address).ToString();
            model.ServiceType = nameof(TcpAdapter);
            return model;
        }
    }
}
