namespace AIPMS.Application.Features.Files;

public sealed record OfficePreviewSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);
public sealed record ZipPreviewEntry(string Path, long SizeBytes, long CompressedBytes, bool IsDirectory);
public sealed record OfficePreviewDto(string Kind, IReadOnlyList<string> Paragraphs,
    IReadOnlyList<OfficePreviewSheet> Sheets, bool Truncated,
    IReadOnlyList<ZipPreviewEntry>? ZipEntries = null);
