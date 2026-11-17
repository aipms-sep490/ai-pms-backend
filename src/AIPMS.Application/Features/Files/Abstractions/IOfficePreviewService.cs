namespace AIPMS.Application.Features.Files.Abstractions;

public interface IOfficePreviewService
{
    OfficePreviewDto Preview(string fileName, byte[] bytes);
}
