using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;

namespace TiaMcpV16
{
    /// <summary>Outils sur le projet ouvert dans TIA Portal V16. Lecture + écriture (projet uniquement, jamais l'automate réel).</summary>
    internal sealed class TiaTools
    {
        private const int MaxChars = 60000;
        private const string PlcArg = "Nom de l'automate ou de l'appareil";
        private const string TargetArg = "cpu | interface | node (node = adresse IP, masque, routeur, nom PROFINET)";

        private Project _project;

        // ---------- Définitions MCP ----------

        public static JArray Definitions() => new JArray
        {
            Tool("tia_status", "Instances TIA Portal V16 lancées et projet ouvert."),
            Tool("list_plcs", "Liste les automates (PLC) du projet ouvert."),
            Tool("list_blocks", "Liste les blocs (OB, FB, FC, DB) d'un automate, avec dossier, numéro, langage, état de compilation.",
                ("plc", PlcArg)),
            Tool("export_block_xml", "Exporte un bloc en XML SimaticML (le bloc doit être compilé).",
                ("plc", PlcArg), ("block", "Nom du bloc")),
            Tool("list_tag_tables", "Liste les tables de variables API d'un automate.", ("plc", PlcArg)),
            Tool("get_tag_table", "Contenu d'une table de variables : nom, type, adresse, commentaire.",
                ("plc", PlcArg), ("table", "Nom de la table de variables")),

            Tool("get_attributes", "Liste les attributs matériels (valeur + Read/ReadWrite) de la CPU, de son interface PROFINET ou du noeud réseau. À appeler AVANT set_attribute.",
                ("plc", PlcArg), ("target", TargetArg)),
            Tool("set_attribute", "ÉCRITURE PROJET. Modifie un attribut matériel (ex : node/Address = IP, node/SubnetMask, node/UseRouter). 'expected' doit être la valeur actuelle exacte, sinon refus. Ne charge rien dans l'automate.",
                ("plc", PlcArg), ("target", TargetArg), ("name", "Nom de l'attribut"),
                ("value", "Nouvelle valeur"), ("expected", "Valeur actuelle (lue avec get_attributes)")),
            Tool("import_scl", "ÉCRITURE PROJET. Crée ou remplace des blocs à partir d'une source SCL (FUNCTION_BLOCK, FUNCTION, DATA_BLOCK, TYPE...). Compile ensuite avec compile.",
                ("plc", PlcArg), ("scl", "Code source SCL complet")),
            Tool("import_block_xml", "ÉCRITURE PROJET. Importe un bloc SimaticML. REMPLACE le bloc s'il existe déjà.",
                ("plc", PlcArg), ("xml", "Document SimaticML complet")),
            Tool("compile", "Compile le logiciel de l'automate et renvoie erreurs et avertissements.", ("plc", PlcArg)),
            Tool("save_project", "Enregistre le projet TIA sur disque."),
        };

        private static JObject Tool(string name, string desc, params (string n, string d)[] ps)
        {
            var props = new JObject();
            foreach (var (n, d) in ps) props[n] = new JObject { ["type"] = "string", ["description"] = d };
            return new JObject
            {
                ["name"] = name,
                ["description"] = desc,
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = props,
                    ["required"] = new JArray(ps.Select(p => p.n))
                }
            };
        }

        // ---------- Dispatch ----------

        public string Call(string name, JObject a)
        {
            switch (name)
            {
                case "tia_status": return Status();
                case "list_plcs": return ListPlcs();
                case "list_blocks": return ListBlocks(Req(a, "plc"));
                case "export_block_xml": return ExportBlock(Req(a, "plc"), Req(a, "block"));
                case "list_tag_tables": return ListTagTables(Req(a, "plc"));
                case "get_tag_table": return GetTagTable(Req(a, "plc"), Req(a, "table"));
                case "get_attributes": return GetAttributes(Req(a, "plc"), Req(a, "target"));
                case "set_attribute": return SetAttribute(Req(a, "plc"), Req(a, "target"), Req(a, "name"), Req(a, "value"), Req(a, "expected"));
                case "import_scl": return ImportScl(Req(a, "plc"), Req(a, "scl"));
                case "import_block_xml": return ImportXml(Req(a, "plc"), Req(a, "xml"));
                case "compile": return Compile(Req(a, "plc"));
                case "save_project": Proj().Save(); return "Projet enregistré : " + Proj().Path.FullName;
                default: throw new ArgumentException("Outil inconnu : " + name);
            }
        }

        private static string Req(JObject a, string key)
        {
            var v = (string)a[key];
            if (string.IsNullOrWhiteSpace(v)) throw new ArgumentException("Argument manquant : " + key);
            return v;
        }

        // ---------- Connexion ----------

        private Project Proj()
        {
            if (_project != null)
            {
                try { var _ = _project.Name; return _project; }
                catch { _project = null; } // projet fermé ou TIA quitté
            }

            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0)
                throw new InvalidOperationException("Aucun TIA Portal V16 lancé. Ouvre TIA Portal et un projet.");

            foreach (var pr in procs)
            {
                var tp = pr.Attach(); // 1re fois : TIA affiche une fenêtre d'autorisation à accepter
                if (tp.Projects.Count > 0) return _project = tp.Projects[0];
            }
            throw new InvalidOperationException("TIA Portal est lancé mais aucun projet n'est ouvert.");
        }

        // ---------- Parcours ----------

        private static IEnumerable<Device> AllDevices(Project p)
        {
            foreach (Device d in p.Devices) yield return d;
            foreach (var d in GroupDevices(p.DeviceGroups)) yield return d;
        }

        private static IEnumerable<Device> GroupDevices(DeviceUserGroupComposition groups)
        {
            foreach (DeviceUserGroup g in groups)
            {
                foreach (Device d in g.Devices) yield return d;
                foreach (var d in GroupDevices(g.Groups)) yield return d;
            }
        }

        private static IEnumerable<(DeviceItem cpu, PlcSoftware plc)> SoftwareIn(DeviceItemComposition items)
        {
            foreach (DeviceItem di in items)
            {
                if (di.GetService<SoftwareContainer>()?.Software is PlcSoftware plc) yield return (di, plc);
                foreach (var s in SoftwareIn(di.DeviceItems)) yield return s;
            }
        }

        private IEnumerable<(Device dev, DeviceItem cpu, PlcSoftware plc)> Plcs()
        {
            foreach (var d in AllDevices(Proj()))
                foreach (var (cpu, plc) in SoftwareIn(d.DeviceItems))
                    yield return (d, cpu, plc);
        }

        private (Device dev, DeviceItem cpu, PlcSoftware plc) FindPlcFull(string name)
        {
            var all = Plcs().ToList();
            var hit = all.FirstOrDefault(x =>
                string.Equals(x.plc.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.dev.Name, name, StringComparison.OrdinalIgnoreCase));
            if (hit.plc != null) return hit;
            throw new ArgumentException($"Automate '{name}' introuvable. Disponibles : " +
                string.Join(", ", all.Select(x => x.plc.Name)));
        }

        private PlcSoftware FindPlc(string name) => FindPlcFull(name).plc;

        private static void WalkBlocks(PlcBlockGroup g, string path, List<(string folder, PlcBlock block)> acc)
        {
            foreach (PlcBlock b in g.Blocks) acc.Add((path, b));
            foreach (PlcBlockUserGroup sg in g.Groups) WalkBlocks(sg, path + "/" + sg.Name, acc);
        }

        private static void WalkTables(PlcTagTableGroup g, string path, List<(string folder, PlcTagTable table)> acc)
        {
            foreach (PlcTagTable t in g.TagTables) acc.Add((path, t));
            foreach (PlcTagTableUserGroup sg in g.Groups) WalkTables(sg, path + "/" + sg.Name, acc);
        }

        private static string Text(MultilingualText m)
        {
            if (m == null) return "";
            foreach (MultilingualTextItem it in m.Items)
                if (!string.IsNullOrEmpty(it.Text)) return it.Text;
            return "";
        }

        private static string Json(JToken t) => t.ToString(Formatting.Indented);

        private static string Truncate(string s) =>
            s.Length <= MaxChars ? s : s.Substring(0, MaxChars) + $"\n<!-- TRONQUÉ : {s.Length} caractères au total -->";

        private static string TempFile(string ext) =>
            Path.Combine(Path.GetTempPath(), "tia-mcp-" + Guid.NewGuid().ToString("N") + ext);

        // ---------- Lecture ----------

        private string Status()
        {
            var arr = new JArray();
            foreach (var pr in TiaPortal.GetProcesses())
                arr.Add(new JObject { ["pid"] = pr.Id, ["projet"] = pr.ProjectPath?.FullName ?? "(aucun)" });
            return Json(new JObject { ["instances"] = arr, ["dossier_openness"] = Program.ApiDir });
        }

        private string ListPlcs()
        {
            var arr = new JArray();
            foreach (var (dev, cpu, plc) in Plcs())
                arr.Add(new JObject
                {
                    ["plc"] = plc.Name, ["appareil"] = dev.Name,
                    ["cpu"] = cpu.GetAttribute("TypeName")?.ToString(), ["type"] = dev.TypeIdentifier
                });
            return Json(new JObject { ["projet"] = Proj().Name, ["automates"] = arr });
        }

        private List<(string, PlcBlock)> Blocks(PlcSoftware plc)
        {
            var acc = new List<(string, PlcBlock)>();
            WalkBlocks(plc.BlockGroup, "", acc);
            return acc;
        }

        private string ListBlocks(string plcName)
        {
            var arr = new JArray();
            foreach (var (folder, b) in Blocks(FindPlc(plcName)))
                arr.Add(new JObject
                {
                    ["dossier"] = folder == "" ? "/" : folder, ["nom"] = b.Name, ["type"] = b.GetType().Name,
                    ["numero"] = b.Number, ["langage"] = b.ProgrammingLanguage.ToString(), ["compile"] = b.IsConsistent
                });
            return Json(arr);
        }

        private string ExportBlock(string plcName, string blockName)
        {
            var plc = FindPlc(plcName);
            var b = Blocks(plc).Select(x => x.Item2)
                        .FirstOrDefault(x => string.Equals(x.Name, blockName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Bloc '{blockName}' introuvable dans '{plc.Name}'.");
            if (!b.IsConsistent)
                throw new InvalidOperationException($"Le bloc '{b.Name}' n'est pas compilé. Utilise l'outil compile d'abord.");

            var path = TempFile(".xml");
            try
            {
                b.Export(new FileInfo(path), ExportOptions.WithDefaults);
                return Truncate(File.ReadAllText(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private List<(string, PlcTagTable)> Tables(PlcSoftware plc)
        {
            var acc = new List<(string, PlcTagTable)>();
            WalkTables(plc.TagTableGroup, "", acc);
            return acc;
        }

        private string ListTagTables(string plcName)
        {
            var arr = new JArray();
            foreach (var (folder, t) in Tables(FindPlc(plcName)))
                arr.Add(new JObject { ["dossier"] = folder == "" ? "/" : folder, ["nom"] = t.Name, ["nb_variables"] = t.Tags.Count });
            return Json(arr);
        }

        private string GetTagTable(string plcName, string tableName)
        {
            var plc = FindPlc(plcName);
            var t = Tables(plc).Select(x => x.Item2)
                        .FirstOrDefault(x => string.Equals(x.Name, tableName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Table '{tableName}' introuvable dans '{plc.Name}'.");
            var arr = new JArray();
            foreach (PlcTag tag in t.Tags)
                arr.Add(new JObject
                {
                    ["nom"] = tag.Name, ["type"] = tag.DataTypeName,
                    ["adresse"] = tag.LogicalAddress, ["commentaire"] = Text(tag.Comment)
                });
            return Json(arr);
        }

        // ---------- Attributs matériels ----------

        private IEngineeringObject Target(string plcName, string target)
        {
            var cpu = FindPlcFull(plcName).cpu;
            switch (target.ToLowerInvariant())
            {
                case "cpu": return cpu;
                case "interface": return PnInterface(cpu).item;
                case "node": return PnInterface(cpu).ni.Nodes.FirstOrDefault()
                                    ?? throw new InvalidOperationException("L'interface n'a aucun noeud réseau.");
                default: throw new ArgumentException("target doit valoir cpu, interface ou node.");
            }
        }

        // ponytail: 1re interface réseau de la CPU uniquement, ajouter un index si CPU à 2 ports PN (1500)
        private static (DeviceItem item, NetworkInterface ni) PnInterface(DeviceItem cpu)
        {
            foreach (DeviceItem di in cpu.DeviceItems)
            {
                var ni = di.GetService<NetworkInterface>();
                if (ni != null && ni.Nodes.Count > 0) return (di, ni);
            }
            throw new InvalidOperationException("Aucune interface réseau trouvée sur la CPU.");
        }

        private static string Show(object v)
        {
            if (v == null) return "";
            var t = v.GetType();
            return t.IsPrimitive || t.IsEnum || v is string || v is DateTime || v is decimal
                ? Convert.ToString(v, CultureInfo.InvariantCulture)
                : "(objet " + t.Name + ")";
        }

        private string GetAttributes(string plcName, string target)
        {
            var o = Target(plcName, target);
            var arr = new JArray();
            foreach (var ai in o.GetAttributeInfos())
            {
                string val;
                try { val = Show(o.GetAttribute(ai.Name)); } catch (Exception ex) { val = "<erreur : " + ex.Message + ">"; }
                arr.Add(new JObject { ["nom"] = ai.Name, ["acces"] = ai.AccessMode.ToString(), ["valeur"] = val });
            }
            return Json(arr);
        }

        private string SetAttribute(string plcName, string target, string name, string value, string expected)
        {
            var o = Target(plcName, target);
            var info = o.GetAttributeInfos().FirstOrDefault(i => i.Name == name)
                       ?? throw new ArgumentException($"Attribut '{name}' inexistant sur {target}. Utilise get_attributes.");
            if (!info.AccessMode.ToString().Contains("Write"))
                throw new InvalidOperationException($"'{name}' est en lecture seule ({info.AccessMode}).");

            var current = o.GetAttribute(name);
            if (!string.Equals(Show(current), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusé : valeur actuelle '{Show(current)}', attendue '{expected}'.");

            o.SetAttribute(name, ConvertTo(current, value));
            return $"{target}/{name} : '{Show(current)}' -> '{Show(o.GetAttribute(name))}'. " +
                   $"Annulation : set_attribute value='{Show(current)}'. Projet non enregistré, non chargé dans l'automate.";
        }

        private static object ConvertTo(object current, string s)
        {
            if (current == null || current is string) return s;
            var t = current.GetType();
            if (t.IsEnum) return Enum.Parse(t, s, true);
            return Convert.ChangeType(s, t, CultureInfo.InvariantCulture);
        }

        // ---------- Écriture de blocs ----------

        private string ImportScl(string plcName, string scl)
        {
            var plc = FindPlc(plcName);
            var before = new HashSet<string>(Blocks(plc).Select(x => x.Item2.Name));
            var path = TempFile(".scl");
            File.WriteAllText(path, scl, new System.Text.UTF8Encoding(true));
            PlcExternalSource src = null;
            try
            {
                src = plc.ExternalSourceGroup.ExternalSources.CreateFromFile(Path.GetFileName(path), path);
                src.GenerateBlocksFromSource();
            }
            finally
            {
                src?.Delete();
                File.Delete(path);
            }
            var created = Blocks(plc).Select(x => x.Item2.Name).Where(n => !before.Contains(n)).ToList();
            return "Source SCL importée. Nouveaux blocs : " + (created.Count > 0 ? string.Join(", ", created) : "aucun (blocs existants remplacés)") +
                   ". Lance compile pour vérifier.";
        }

        private string ImportXml(string plcName, string xml)
        {
            var plc = FindPlc(plcName);
            var path = TempFile(".xml");
            File.WriteAllText(path, xml, new System.Text.UTF8Encoding(true));
            try
            {
                var blocks = plc.BlockGroup.Blocks.Import(new FileInfo(path), ImportOptions.Override);
                return "Importé : " + string.Join(", ", blocks.Select(b => b.Name)) + ". Lance compile pour vérifier.";
            }
            finally { File.Delete(path); }
        }

        private string Compile(string plcName)
        {
            var plc = FindPlc(plcName);
            var r = plc.GetService<ICompilable>().Compile();
            var msgs = new JArray();
            Collect(r.Messages, msgs);
            return Json(new JObject
            {
                ["etat"] = r.State.ToString(), ["erreurs"] = r.ErrorCount,
                ["avertissements"] = r.WarningCount, ["messages"] = msgs
            });
        }

        private static void Collect(CompilerResultMessageComposition list, JArray acc)
        {
            foreach (CompilerResultMessage m in list)
            {
                if (m.State != CompilerResultState.Success && !string.IsNullOrEmpty(m.Description) && acc.Count < 100)
                    acc.Add(new JObject { ["etat"] = m.State.ToString(), ["chemin"] = m.Path, ["message"] = m.Description });
                Collect(m.Messages, acc);
            }
        }
    }
}
