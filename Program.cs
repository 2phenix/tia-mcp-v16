using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TiaMcpV16
{
    internal static class Program
    {
        internal static string ApiDir =>
            Environment.GetEnvironmentVariable("TIA_OPENNESS_DIR")
            ?? @"C:\Program Files\Siemens\Automation\Portal V16\PublicAPI\V16";

        private static int Main()
        {
            // Doit être enregistré AVANT tout usage d'un type Siemens
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSiemens;

            var utf8 = new UTF8Encoding(false);
            var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
            var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };

            Run(stdin, stdout);
            return 0;
        }

        private static Assembly ResolveSiemens(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name).Name;
            var path = Path.Combine(ApiDir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        // Méthode séparée : les types Siemens ne sont résolus qu'ici, après l'enregistrement du handler
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run(StreamReader stdin, StreamWriter stdout)
        {
            var server = new McpServer(new TiaTools());
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JObject resp = server.Handle(line);
                if (resp != null) stdout.WriteLine(resp.ToString(Formatting.None));
            }
        }

        internal static void Log(string msg) => Console.Error.WriteLine("[tia-mcp-v16] " + msg);
    }
}
