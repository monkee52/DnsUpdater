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
                IDictionary<string, string> parsedArgs = ParseArgs(args);

                string configPath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "DnsUpdater.json");

                bool isUsingConfigFile = false;
                string updaterName;
                IDictionary<string, object> updaterConfig = null;

                if (parsedArgs.ContainsKey("configPath")) { // Explicit config
                    configPath = parsedArgs["configPath"];

                    if (!File.Exists(configPath)) {
                        Console.WriteLine("Unable to find config file '{0}'", configPath);

                        return -1;
                    }

                    isUsingConfigFile = true;
                } else if (!parsedArgs.ContainsKey("updaterName")) { // Implicit config
                    if (!File.Exists(configPath)) {
                        ShowHelpMessage();

                        return -1;
                    }

                    isUsingConfigFile = true;
                }

                if (isUsingConfigFile) {
                    // Read config
                    string configData = await File.ReadAllTextAsync(configPath);
                    Config config = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(configData);

                    updaterName = config.updaterName;
                    updaterConfig = config.updaterConfig;
                } else {
                    updaterName = parsedArgs["updaterName"];
                }

                // Get all updaters, excluding IUpdater as it's an interface
                IEnumerable<Type> updaters = GetUpdaters();

                Console.WriteLine("Found updaters {0}", String.Join(", ", updaters.Select(u => u.Name)));

                // Filter to requested updater
                Type updaterType = updaters.FirstOrDefault(u => String.Equals(u.Name, updaterName, StringComparison.InvariantCultureIgnoreCase));

                Console.WriteLine("Found requested updater {0}", updaterType.Name);

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

                Console.WriteLine("{0} takes options {1}", updaterType.Name, String.Join(", ", paramNames));

                object[] initParams = new object[paramNames.Length];

                // Null params
                for (int i = 0; i < initParams.Length; i++) {
                    initParams[i] = Type.Missing;
                }

                if (isUsingConfigFile) { // Apply each updaterConfig key to the constructor
                    foreach (KeyValuePair<string, object> param in updaterConfig) {
                        string paramName = param.Key;

                        // Find parameter index - case insensitive
                        int paramIdx = Array.FindIndex(paramNames, p => String.Equals(paramName, p, StringComparison.InvariantCultureIgnoreCase));

                        initParams[paramIdx] = Convert.ChangeType(param.Value, paramInfos[paramIdx].ParameterType);
                    }
                } else { // Search through all constructor params and check if arg passed
                    for (int i = 0; i < paramInfos.Length; i++) {
                        string paramName = paramInfos[i].Name;

                        if (parsedArgs.ContainsKey(paramName)) {
                            try {
                                initParams[i] = Convert.ChangeType(parsedArgs[paramName], paramInfos[i].ParameterType);
                            } catch (InvalidCastException e) {
                                if (paramInfos[i].ParameterType == typeof(bool)) { // The prescence of a param can implicitly enable the parameter
                                    initParams[i] = true;
                                } else {
                                    throw e;
                                }
                            }
                        }
                    }
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

            private static Type[] GetUpdaters() {
                return AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).Where(t => typeof(IUpdater).IsAssignableFrom(t) && t != typeof(IUpdater)).ToArray();
            }

            private static int Main(string[] args) {
                return Program.MainAsync(args).GetAwaiter().GetResult();
            }

            private static void ShowHelpMessage() {
                Console.WriteLine("Usage: {0} ([/ConfigPath[:]<PathToConfig.json>]|/UpdaterName[:]<UpdaterName> [...])", Path.GetFileName(Assembly.GetEntryAssembly().Location));
                Console.WriteLine("");
                Console.WriteLine("Available updaters: ");

                foreach (Type t in GetUpdaters()) {
                    Console.WriteLine("     - {0}; Arguments:", t.Name);

                    // Interfaces can't specify that a type needs a static member, so use reflection to invoke it.
                    IDictionary<string, string> helpMessages = (IDictionary<string, string>)t.InvokeMember("GetHelpMessages", BindingFlags.Static | BindingFlags.Public | BindingFlags.InvokeMethod, null, null, null);

                    // Get all constructor params
                    ParameterInfo[] paramInfos = t.GetConstructors()[0].GetParameters();

                    foreach (ParameterInfo info in paramInfos) {
                        string helpMessage = "";

                        if (helpMessages.ContainsKey(info.Name)) {
                            helpMessage = helpMessages[info.Name];
                        }

                        if (info.ParameterType == typeof(bool)) { // The prescence of a param can implicitly enable the parameter
                            Console.WriteLine("          /{0}[:][Value]\t{1}", info.Name, helpMessage);
                        } else {
                            Console.WriteLine("          /{0}[:]<Value>\t{1}", info.Name, helpMessage);
                        }
                    }

                    Console.WriteLine("");
                }
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

            private static IDictionary<string, string> ParseArgs(string[] args) {
                IDictionary<string, string> outputArgs = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

                int idx = 0;

                string argName = null;

                while (idx < args.Length) {
                    if (args[idx][0] == '/') {
                        int idx2 = args[idx].IndexOf(':');

                        if (idx2 == -1) {
                            argName = args[idx].Substring(1);

                            outputArgs[argName] = null;

                            ++idx;
                        } else {
                            outputArgs[args[idx].Substring(1, idx2 - 1)] = args[idx].Substring(idx2 + 1);

                            ++idx;
                        }
                    } else {
                        if (argName != null) {
                            outputArgs[argName] = args[idx];

                            argName = null;
                            ++idx;
                        }
                    }
                }

                return outputArgs;
            }
        }
    }
}
