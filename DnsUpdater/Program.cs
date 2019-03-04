using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace AydenIO {
    namespace DnsUpdater {
        class Program {
            private const string GET_EXTERNAL_IP_URI = "https://icanhazip.com/";

            static async Task<int> MainAsync(string[] args) {
                string configPath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "DnsUpdater.json");

                // Config path passed in arguments
                if (args.Length == 1) {
                    configPath = args[0];
                }

                if (!File.Exists(configPath)) {
                    Console.WriteLine("Unable to find config. Create DnsUpdater.json or specify first argument as path to config.");
                    return -1;
                }

                // Read config
                string configData = await File.ReadAllTextAsync(configPath);
                Config config = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(configData);

                // Split
                string updaterName = config.updaterName;
                IDictionary<string, object> updaterConfig = config.updaterConfig;

                // Get all updaters, excluding IUpdater as it's an interface
                IEnumerable<Type> updaters = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).Where(t => typeof(IUpdater).IsAssignableFrom(t) && t != typeof(IUpdater));

                Debug.WriteLine(String.Format("Found updaters {0}", String.Join(", ", updaters.Select(u => u.Name))));

                // Filter to requested updater
                Type updaterType = updaters.FirstOrDefault(u => String.Equals(u.Name, updaterName, StringComparison.InvariantCultureIgnoreCase));

                Debug.WriteLine(String.Format("Found requested updater {0}", updaterType.Name));

                if (updaterType == null) {
                    // Updater not found
                    return -1;
                }

                ConstructorInfo[] constructors = updaterType.GetConstructors();

                if (constructors.Length > 1) {
                    // Too many to differentiate between
                    return -1;
                }

                // Find constructors
                ConstructorInfo constructor = constructors[0];

                ParameterInfo[] paramInfos = constructor.GetParameters();
                string[] paramNames = paramInfos.Select(p => p.Name).ToArray();

                Debug.WriteLine(String.Format("{0} takes options {1}", updaterType.Name, String.Join(", ", paramNames)));

                object[] initParams = new object[paramNames.Length];

                // Null params
                for (int i = 0; i < initParams.Length; i++) {
                    initParams[i] = Type.Missing;
                }

                foreach (KeyValuePair<string, object> param in updaterConfig) {
                    string paramName = param.Key;

                    // Find parameter index - case insensitive
                    int paramIdx = Array.FindIndex(paramNames, p => String.Equals(paramName, p, StringComparison.InvariantCultureIgnoreCase));

                    initParams[paramIdx] = Convert.ChangeType(param.Value, paramInfos[paramIdx].ParameterType);
                }

                // Create updater
                IUpdater updater = (IUpdater)constructor.Invoke(initParams);

                if (updater == null) {
                    // Unable to create updater
                    return -1;
                }

                // Perform update
                await updater.Update(Program.GetExternalIp);

#if DEBUG
                // Pause
                Program.Pause();
#endif

                return 0;
            }

            private static int Main(string[] args) {
                return Program.MainAsync(args).GetAwaiter().GetResult();
            }

            private static async Task<IPAddress> GetExternalIp() {
                string externalIpString;

                using (WebClient webClient = new WebClient()) {
                    externalIpString = await webClient.DownloadStringTaskAsync(new Uri(GET_EXTERNAL_IP_URI));
                }

                externalIpString = externalIpString.Trim();

                IPAddress externalIp;

                try {
                    externalIp = IPAddress.Parse(externalIpString);
                } catch (FormatException) {
                    return null;
                }

                return externalIp;
            }

            private static void Pause() {
                Console.Write("Press any key to continue...");
                Console.ReadKey(true);
                Console.WriteLine();
            }
        }
    }
}
