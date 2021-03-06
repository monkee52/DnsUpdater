﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AydenIO {
    namespace DnsUpdater {
        class DuckDnsUpdater : IUpdater {
            private const string UPDATE_URI = "https://www.duckdns.org/update?domains={0}&token={1}";

            private string domain;
            private string token;

            public DuckDnsUpdater(string domain, string token) {
                this.domain = domain;
                this.token = token;
            }

            public static IDictionary<string, string> GetHelpMessages() {
                return new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) {
                    ["domain"] = "The FQDN of the domain to update.",
                    ["token"] = "The DuckDNS API token to authenticate the update."
                };
            }

            public async Task Update(GetExternalIpAddressHandler getExternalIp) {
                string updateUri = String.Format(UPDATE_URI, this.domain, this.token);

                using (WebClient webClient = new WebClient()) {
                    await webClient.DownloadStringTaskAsync(new Uri(updateUri));
                }
            }
        }
    }
}
