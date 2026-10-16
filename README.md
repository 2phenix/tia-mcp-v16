# tia-mcp-v16

Serveur MCP pour **Siemens TIA Portal V16**, via l'API Openness.
Permet à Claude (ou tout client MCP) de lire et modifier le projet ouvert dans TIA.

> Les serveurs MCP TIA existants (TiaCommander, T-IA Connect, TiaMcpServer) ne supportent que V17 et plus. Celui-ci cible V16.

## Outils

| Outil | Rôle | Modifie le projet TIA ? |
|---|---|---|
| `tia_status` | Instances TIA lancées + projet ouvert | non |
| `list_plcs` | Automates du projet | non |
| `list_blocks` | Blocs d'un automate (dossier, type, numéro, langage, compilé ?) | non |
| `export_block_xml` | Export SimaticML d'un bloc compilé | non |
| `list_tag_tables` / `get_tag_table` | Tables de variables et leur contenu | non |
| `get_attributes` | Attributs matériels de `cpu`, `interface` ou `node` (valeur + accès) | non |
| `set_attribute` | Modifie un attribut (IP, masque, routeur, nom PROFINET...) | **oui** |
| `import_scl` | Crée ou remplace des blocs depuis une source SCL | **oui** |
| `import_block_xml` | Importe un bloc SimaticML (remplace l'existant) | **oui** |
| `compile` | Compile le logiciel, renvoie erreurs et avertissements | **oui** |
| `save_project` | Enregistre le projet | **oui** |

Garde-fous :
- `set_attribute` exige la valeur actuelle (`expected`). Si elle ne correspond pas, refus. Les attributs en lecture seule sont refusés.
- Rien n'est jamais **chargé dans l'automate réel**. Le chargement reste manuel dans TIA.
- `export_block_xml` refuse un bloc non compilé (contrainte Openness).

## Limites connues (testées sur CPU 1214C, V16 Update 8)

Openness V16 **n'expose pas** : niveau de protection / mot de passe, serveur web, et la plupart des réglages de l'onglet Propriétés de la CPU.
Vérifié par comparaison de tous les attributs de la station avant / après modification dans TIA (`snapshot.ps1`) : 0 différence.

Modifiable en V16 :
- **node** : `Address`, `SubnetMask`, `UseRouter`, `IpProtocolSelection`, `PnDeviceNameAutoGeneration`, `PnDeviceNameSetDirectly`, `UseIsoProtocol`
- **interface** : `InterfaceOperatingMode`, `PnSendClock`, `Name`
- **cpu** : `Name`, `Comment`, `Author`, `PlantDesignation`, `LocationIdentifier`

Seule la 1re interface réseau de la CPU est gérée.

## Installation

### 1. Prérequis

- Windows, TIA Portal **V16** avec le composant **Openness** installé
- [.NET SDK](https://dotnet.microsoft.com/download) 8 ou plus (`dotnet --version`)
- Claude Desktop (ou un autre client MCP stdio)

### 2. Autoriser Openness

En invite de commandes **administrateur** :

```
net localgroup "Siemens TIA Openness" %USERNAME% /add
```

Puis **fermer la session Windows et se reconnecter**. Vérifier :

```
whoami /groups | findstr Openness
```

### 3. Compiler

```
git clone https://github.com/2phenix/tia-mcp-v16.git
cd tia-mcp-v16
dotnet build -c Release
```

Exécutable : `bin\Release\net48\tia-mcp-v16.exe`

Si l'API Openness n'est pas dans `C:\Program Files\Siemens\Automation\Portal V16\PublicAPI\V16` :

```
dotnet build -c Release -p:TiaApiDir="D:\chemin\PublicAPI\V16"
```

### 4. Brancher sur Claude Desktop

`%APPDATA%\Claude\claude_desktop_config.json` :

```json
{
  "mcpServers": {
    "tia-v16": {
      "command": "C:\\chemin\\vers\\tia-mcp-v16\\bin\\Release\\net48\\tia-mcp-v16.exe",
      "env": {
        "TIA_OPENNESS_DIR": "C:\\Program Files\\Siemens\\Automation\\Portal V16\\PublicAPI\\V16"
      }
    }
  }
}
```

Quitter complètement Claude Desktop (icône de la barre des tâches) puis le relancer.

### 5. Utiliser

1. Lancer TIA Portal V16 et ouvrir un projet (**une copie**, pas l'original).
2. Demander à Claude : « liste les automates du projet TIA ».
3. Au premier appel, TIA affiche une **fenêtre d'autorisation Openness** : accepter. Elle revient après chaque recompilation de l'exe.

## Tests

| Script | Rôle |
|---|---|
| `test-write.ps1` | Smoke test d'écriture sur un automate `PLC_1` : change l'IP puis la remet, vérifie les refus, importe un FC SCL, compile. **Modifie le projet ouvert.** |
| `snapshot.ps1 -out fichier.txt` | Dump de tous les attributs de la station. Deux dumps + `Compare-Object` = ce qu'Openness voit vraiment. |

Test manuel du protocole :

```
echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"tia_status","arguments":{}}} | bin\Release\net48\tia-mcp-v16.exe
```

## Structure

- `Program.cs` : boucle stdio, chargement des DLL Siemens depuis l'installation TIA
- `McpServer.cs` : JSON-RPC 2.0 / MCP (initialize, tools/list, tools/call), sans SDK
- `TiaTools.cs` : les outils, sur l'API Openness
