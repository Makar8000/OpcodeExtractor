using System.IO.Compression;
using System.Reflection;
using Mono.Cecil;

namespace OpcodeExtractor;

public static class OpcodePatcher
{
  private static string tempPath = Path.GetFullPath("temp");
  private static string pluginPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker\\Plugins\\FFXIV_ACT_Plugin.dll");

  public static bool ReplaceOpcodes() {
    // Load opcodes from text file
    var opcodesText = File.ReadAllText("opcodes-machina.txt");
    if (opcodesText.Length == 0) {
      Console.WriteLine("Opcode text file is empty");
      return false;
    }

    // Duplicate plugin for loading
    try {
      DeleteTempFiles(true);
      Directory.CreateDirectory(tempPath);
      File.Copy(pluginPath, Path.Combine(tempPath, "FFXIV_ACT_Plugin.dll"), true);
    } catch {
      Console.WriteLine("Failed to duplicate FFXIV Plugin. Check that the plugin exists.");
      return false;
    }

    // Load FFXIV Assembly
    Assembly ffxiv = Assembly.LoadFile(Path.Combine(tempPath, "FFXIV_ACT_Plugin.dll"));
    if (ffxiv.FullName == null || !ffxiv.FullName.StartsWith("FFXIV_ACT_Plugin,")) {
      Console.WriteLine("Failed to load assembly.");
      return false;
    }

    // Decompress machina to temp.dll
    using (var compressedStream = ffxiv.GetManifestResourceStream("costura.machina.ffxiv.dll.compressed")) {
      if (compressedStream == null) {
        Console.WriteLine("Failed to load compressed machina.");
        return false;
      }
      using (var destinationFileStream = File.Create(Path.Combine(tempPath, "temp.dll"))) {
        using (var decompressionStream = new DeflateStream(compressedStream, CompressionMode.Decompress)) {
          decompressionStream.CopyTo(destinationFileStream);
        }
      }
    }

    // load decompressed machina (temp.dll)
    var mchDef = AssemblyDefinition.ReadAssembly(Path.Combine(tempPath, "temp.dll"));
    var mchResources = mchDef.MainModule.Resources;
    var mchSelResource = mchResources.FirstOrDefault(x => x.Name == "Machina.FFXIV.Headers.Opcodes.Global.txt");
    if (mchSelResource == null) {
      Console.WriteLine("Failed to load global opcode resource.");
      return false;
    }
    // load corrected opcodes
    using (var txtStream = GenerateStreamFromString(opcodesText)) {
      // replace resource
      var newResource = new EmbeddedResource("Machina.FFXIV.Headers.Opcodes.Global.txt", mchSelResource.Attributes, txtStream);
      mchResources.Remove(mchSelResource);
      mchResources.Add(newResource);
      // write corrected decompressed machina to temp2.dll
      mchDef.Write(Path.Combine(tempPath, "temp2.dll"));
    }
    // read corrected decompressed machina (temp2.dll) and compress it
    using (var originalFileStream = File.OpenRead(Path.Combine(tempPath, "temp2.dll"))) {
      using (var destinationFileStream = File.Create(Path.Combine(tempPath, "costura.machina.ffxiv.dll.compressed"))) {
        using (var compressionStream = new DeflateStream(destinationFileStream, CompressionMode.Compress)) {
          originalFileStream.CopyTo(compressionStream);
        }
      }
    }

    // load ffxiv plugin
    var ffxivDef = AssemblyDefinition.ReadAssembly(Path.Combine(tempPath, "FFXIV_ACT_Plugin.dll"));
    var ffxivResources = ffxivDef.MainModule.Resources;
    var ffxivSelResource = ffxivResources.FirstOrDefault(x => x.Name == "costura.machina.ffxiv.dll.compressed");
    if (ffxivSelResource == null) {
      Console.WriteLine("Failed to load compressed machina.");
      return false;
    }
    // load corrected machina
    using (var filestream = File.OpenRead(Path.Combine(tempPath, "costura.machina.ffxiv.dll.compressed"))) {
      // replace resource
      var newResource = new EmbeddedResource("costura.machina.ffxiv.dll.compressed", ffxivSelResource.Attributes, filestream);
      ffxivResources.Remove(ffxivSelResource);
      ffxivResources.Add(newResource);
      // write corrected ffxiv plugin
      ffxivDef.Write("FFXIV_ACT_Plugin.dll");
      using (ZipArchive zip = ZipFile.Open("FFXIV_ACT_Plugin.zip", ZipArchiveMode.Create)) {
        zip.CreateEntryFromFile("FFXIV_ACT_Plugin.dll", "FFXIV_ACT_Plugin.dll");
      }
    }

    DeleteTempFiles();
    Console.WriteLine("Plugin updated successfully");
    return true;
  }

  private static Stream GenerateStreamFromString(string str) {
    var stream = new MemoryStream();
    var writer = new StreamWriter(stream);
    writer.Write(str);
    writer.Flush();
    stream.Position = 0;
    return stream;
  }

  private static void DeleteTempFiles(bool cleanOutput = false) {
    try {
      if (cleanOutput) {
        File.Delete("FFXIV_ACT_Plugin.dll");
        File.Delete("FFXIV_ACT_Plugin.zip");
      }
      Directory.GetFiles(tempPath).ToList().ForEach(File.Delete);
    } catch { }
  }
}