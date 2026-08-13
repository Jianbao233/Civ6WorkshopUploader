using System.Text.Json.Serialization;

namespace Civ6WorkshopUploader;

[JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = true)]
[JsonSerializable(typeof(ModConfig))]
[JsonSerializable(typeof(LocalizationConfig))]
[JsonSerializable(typeof(List<LocalizationConfig>))]
[JsonSerializable(typeof(WorkshopComment))]
[JsonSerializable(typeof(List<WorkshopComment>))]
[JsonSerializable(typeof(CommentExport))]
internal partial class SourceGenerationContext : JsonSerializerContext { }