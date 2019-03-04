using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DnsUpdater {
    public delegate Task<IPAddress> GetExternalIpAddressHandler();

    interface IUpdater {
        Task Update(GetExternalIpAddressHandler getExternalIp);
    }
}
