namespace Civ6WorkshopUploader;

public class ModConfig
{
  public string? title;
  public string? description;
  public string? visibility;
  public string? changeNote;
  public List<string>? tags;
  public List<ulong>? dependencies;
  public string? minBranch;
  public string? maxBranch;

  /// <summary>
  /// Optional language variants. Each entry is uploaded as a separate
  /// SubmitItemUpdate that only mutates title / description / changeNote
  /// for the specified Steam language code (e.g. "english", "schinese",
  /// "tchinese", "japanese", "koreana", ...).
  ///
  /// Content (the contents of the `content/` directory) and the preview
  /// image are language-agnostic and therefore are NOT re-uploaded for
  /// each variant; they are only sent once during the primary update,
  /// which is forced to write to the "english" default language so that
  /// the default workshop variant is deterministic regardless of the
  /// uploader's current Steam client language.
  /// </summary>
  public List<LocalizationConfig>? localizations;
}

public class LocalizationConfig
{
  /// <summary>Steam language code, e.g. "english", "schinese". Required.</summary>
  public string? language;

  public string? title;
  public string? description;
  public string? changeNote;
}