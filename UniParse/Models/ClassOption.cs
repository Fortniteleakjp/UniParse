namespace UniParse.Models;

/// <summary>One entry in the class (asset type) filter dropdown.</summary>
/// <param name="Display">Label shown in the combo box, e.g. "Texture2D (316)".</param>
/// <param name="ClassName">The native class name to filter by, or null for "all classes".</param>
public sealed record ClassOption(string Display, string? ClassName);
