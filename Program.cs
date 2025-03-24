using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpcodeExtractor;

public enum OutputFormat
{
    All = -1,
    FFXIV_ACT_Plugin,
    OverlayPlugin
}

public class Program
{
    static async Task<int> Main(string[] args)
    {
        var opcodeFileMapArgument = new Argument<FileInfo?>(
            name: "opcodeMapFile",
            description: "The opcode map to use. Default = \"resources/global.jsonc\"");
        var gameExecutableArgument = new Argument<FileInfo?>(
            name: "gameExecutable",
            description: "The game executable to map. Default = \"C:\\Program Files (x86)\\SquareEnix\\FINAL FANTASY XIV - A Realm Reborn\\game\\ffxiv_dx11.exe\"");
        var dumpAllOpcodesArgument = new Argument<bool>(
            name: "dumpAllOpcodes",
            description: "Should all opcodes be dumped or just those specified in the map file. Default = \"False\"");
        var outputFormatArgument = new Argument<OutputFormat>(
            name: "outputFormat",
            description: "Which output format to use. Default = \"All\"");
        var inputMapKeyArgument = new Argument<string>(
            name: "inputMapKey",
            description: "Which opcode map key to use. Default = \"FFXIV_ACT_Plugin\"");
        opcodeFileMapArgument.SetDefaultValue(new FileInfo(@"resources/global.jsonc"));
        gameExecutableArgument.SetDefaultValue(new FileInfo(@"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\ffxiv_dx11.exe"));
        dumpAllOpcodesArgument.SetDefaultValue(false);
        outputFormatArgument.SetDefaultValue(OutputFormat.FFXIV_ACT_Plugin);
        inputMapKeyArgument.SetDefaultValue("FFXIV_ACT_Plugin");

        var rootCommand = new RootCommand("Map opcodes as defined in opcodeMapFile for executable gameExecutable");
        rootCommand.AddArgument(opcodeFileMapArgument);
        rootCommand.AddArgument(gameExecutableArgument);
        rootCommand.AddArgument(dumpAllOpcodesArgument);
        rootCommand.AddArgument(outputFormatArgument);
        rootCommand.AddArgument(inputMapKeyArgument);

        rootCommand.SetHandler(CommandHandler, opcodeFileMapArgument, gameExecutableArgument, dumpAllOpcodesArgument, outputFormatArgument, inputMapKeyArgument);

        var actResult = await rootCommand.InvokeAsync(args);

        outputFormatArgument.SetDefaultValue(OutputFormat.OverlayPlugin);
        inputMapKeyArgument.SetDefaultValue("OverlayPlugin");
        var opResult = await rootCommand.InvokeAsync(args);

        return Math.Max(actResult, opResult);
    }

    private static void CommandHandler(FileInfo? opcodeMapFile, FileInfo? gameExecutable, bool dumpAllOpcodes, OutputFormat outputFormat, string inputMapKey)
    {
        var opcodeMapData = JsonSerializer.Deserialize<JsonNode>(File.ReadAllText(opcodeMapFile!.FullName), new JsonSerializerOptions()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
        });
        if (opcodeMapData == null)
        {
            Console.Error.WriteLine("Invalid opcodes file");
            return;
        }

        var opcodes = ExtractOpcodes(opcodeMapData, gameExecutable!, dumpAllOpcodes, inputMapKey);

        var sortOrder = opcodeMapData[inputMapKey]?.AsObject().ToDictionary().Keys.ToArray();
        if (sortOrder == null)
        {
            Console.Error.WriteLine("Invalid data type for \"map\" in opcodes file");
            sortOrder = [];
        }
        OutputOpcodes(opcodes, outputFormat, sortOrder);
    }

    private static void OutputOpcodes(Dictionary<int, string> opcodes, OutputFormat outputFormat, string[] sortOrder)
    {
        if (outputFormat == OutputFormat.All || outputFormat == OutputFormat.FFXIV_ACT_Plugin)
        {
            OutputOpcodesForFFXIV_ACT_Plugin(opcodes, sortOrder);
            try { OpcodePatcher.ReplaceOpcodes(); } catch { }
        }
        if (outputFormat == OutputFormat.All || outputFormat == OutputFormat.OverlayPlugin)
        {
            OutputOpcodesForOverlayPlugin(opcodes, sortOrder);
        }
    }

    private static void OutputOpcodesForFFXIV_ACT_Plugin(Dictionary<int, string> opcodes, string[] sortOrder)
    {
        using (var sw = new StreamWriter("opcodes-machina.txt", false))
        {
            var entries = opcodes.ToList().OrderBy(x => Array.IndexOf(sortOrder, x.Value)).ToList();
            for (int i = 0; i < entries.Count; i++) {
                sw.Write($"{entries[i].Value}|{entries[i].Key:x}");
                if(i < entries.Count - 1)
                {
                    sw.WriteLine();
                }
            }
        }
    }

    private static void OutputOpcodesForOverlayPlugin(Dictionary<int, string> opcodes, string[] sortOrder)
    {
        Dictionary<string, Dictionary<string, int>?> overlayPluginMap = [];

        foreach (var key in sortOrder)
        {
            overlayPluginMap[key] = null;
        }

        foreach (var entry in opcodes)
        {
            Dictionary<string, int> opEntry = new Dictionary<string, int>()
            {
                ["opcode"] = entry.Key,
                ["size"] = 0,
            };

            overlayPluginMap[entry.Value] = opEntry;
        }

        using (var sw = new StreamWriter("opcodes-overlayp.json", false))
        {
            sw.Write(JsonSerializer.Serialize(overlayPluginMap, new JsonSerializerOptions() 
            {
                WriteIndented = true
            }));
        }
    }

    /// <summary>
    /// Map opcodes as defined in opcodeMapFile for executable gameExecutable
    /// </summary>
    /// <param name="opcodeMapData">The opcode map to use</param>
    /// <param name="gameExecutable">The game executable to map</param>
    /// <param name="dumpAllOpcodes">Whether to dump all opcodes, or just the mapped opcodes</param>
    public static Dictionary<int, string> ExtractOpcodes(JsonNode opcodeMapData, FileInfo gameExecutable, bool dumpAllOpcodes, string inputMapKey)
    {
        var opcodeMethod = opcodeMapData["method"]?.ToString() ?? "";

        byte[] gameData = File.ReadAllBytes(gameExecutable.FullName);
        switch (opcodeMethod)
        {
            case "vtable":
                return OpcodeExtractorVTable.Extract(opcodeMapData, gameData, dumpAllOpcodes, inputMapKey);
        }
        return [];
    }
}