using System;
using System.Collections.Generic;
using System.Text;

namespace AydenIO {
    namespace DnsUpdater {
        struct Config {
            public string updaterName;
            public IDictionary<string, object> updaterConfig;
        }
    }
}
