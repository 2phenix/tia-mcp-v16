using System;
using Newtonsoft.Json.Linq;

namespace TiaMcpV16
{
    /// <summary>JSON-RPC 2.0 sur stdio, sous-ensemble MCP : initialize, ping, tools/list, tools/call.</summary>
    internal sealed class McpServer
    {
        private readonly TiaTools _tools;

        public McpServer(TiaTools tools) { _tools = tools; }

        public JObject Handle(string line)
        {
            JObject req;
            try { req = JObject.Parse(line); }
            catch (Exception ex) { return Error(JValue.CreateNull(), -32700, "Parse error: " + ex.Message); }

            JToken id = req["id"];
            string method = (string)req["method"];

            // Notification (pas d'id) : aucune réponse
            if (id == null) return null;

            try
            {
                switch (method)
                {
                    case "initialize":
                        string pv = (string)req["params"]?["protocolVersion"] ?? "2024-11-05";
                        return Result(id, new JObject
                        {
                            ["protocolVersion"] = pv,
                            ["capabilities"] = new JObject { ["tools"] = new JObject() },
                            ["serverInfo"] = new JObject { ["name"] = "tia-v16", ["version"] = "0.1.0" }
                        });

                    case "ping":
                        return Result(id, new JObject());

                    case "tools/list":
                        return Result(id, new JObject { ["tools"] = TiaTools.Definitions() });

                    case "tools/call":
                        var p = req["params"] as JObject ?? new JObject();
                        string name = (string)p["name"];
                        var args = p["arguments"] as JObject ?? new JObject();
                        string text;
                        bool isError = false;
                        try { text = _tools.Call(name, args); }
                        catch (Exception ex) { text = Unwrap(ex); isError = true; Program.Log(text); }
                        return Result(id, new JObject
                        {
                            ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }),
                            ["isError"] = isError
                        });

                    default:
                        return Error(id, -32601, "Method not found: " + method);
                }
            }
            catch (Exception ex)
            {
                return Error(id, -32603, Unwrap(ex));
            }
        }

        private static string Unwrap(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message;
        }

        private static JObject Result(JToken id, JObject result) =>
            new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

        private static JObject Error(JToken id, int code, string message) =>
            new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new JObject { ["code"] = code, ["message"] = message }
            };
    }
}
