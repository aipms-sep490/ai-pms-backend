using System.Text.Json;

if (args.Length < 2) return 1;
var inputPath = args[0];
var outputPath = args[1];
var json = await File.ReadAllTextAsync(inputPath);
using var doc = JsonDocument.Parse(json);
var options = new JsonSerializerOptions { WriteIndented = true };
var formatted = JsonSerializer.Serialize(doc.RootElement, options);
await File.WriteAllTextAsync(outputPath, formatted.Replace("\r\n", "\n") + "\n");
return 0;
