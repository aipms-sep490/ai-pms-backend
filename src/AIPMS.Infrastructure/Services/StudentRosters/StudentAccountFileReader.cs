using System.IO;
using AIPMS.Application.Features.StudentRosters;

namespace AIPMS.Infrastructure.Services.StudentRosters;

internal static class StudentAccountFileReader
{
    public static IReadOnlyList<StudentAccountImportRow> Read(byte[] bytes, string fileName)
    {
        if (bytes.Length == 0 || bytes.Length > CurriculumFileReader.MaxBytes) throw CurriculumFileReader.Invalid("IMPORT_FILE_SIZE_INVALID");
        try
        {
            var rows = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".csv" => CurriculumFileReader.ReadCsv(bytes),
                ".xlsx" => CurriculumFileReader.ReadExcel(bytes),
                _ => throw CurriculumFileReader.Invalid("IMPORT_FORMAT_REQUIRED_XLSX_OR_CSV")
            };
            if (rows.Count < 2) throw CurriculumFileReader.Invalid("IMPORT_NO_DATA");
            var headers = rows[0].Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
            int Column(bool required, params string[] names)
            {
                var found = headers.Select((name, index) => (name, index)).Where(x => names.Contains(x.name, StringComparer.OrdinalIgnoreCase)).ToArray();
                if (found.Length > 1 || required && found.Length != 1) throw CurriculumFileReader.Invalid("IMPORT_HEADERS_REQUIRE_MSSV_NAME_EMAIL");
                return found.Length == 0 ? -1 : found[0].index;
            }
            var student = Column(true, "MSSV", "studentCode");
            var name = Column(true, "Ho ten", "Họ tên", "Họ và tên", "fullName");
            var email = Column(true, "Email");
            var phone = Column(false, "SDT", "SĐT", "phone");
            var curriculum = Column(false, "Khung", "curriculumCode");
            var output = new List<StudentAccountImportRow>();
            for (var i = 1; i < rows.Count; i++)
            {
                if (rows[i].All(string.IsNullOrWhiteSpace)) continue;
                string Value(int column) => column >= 0 && column < rows[i].Length ? rows[i][column].Trim() : "";
                output.Add(new(i + 1, Value(student), Value(name), Value(email), Value(phone), Value(curriculum)));
            }
            if (output.Count is < 1 or > 500) throw CurriculumFileReader.Invalid("IMPORT_REQUIRES_1_TO_500_ROWS");
            return output;
        }
        catch (Exception ex) when (ex is not AIPMS.Application.Common.Exceptions.ValidationException
            && ex is not OutOfMemoryException && ex is not OperationCanceledException)
        { throw CurriculumFileReader.Invalid("IMPORT_FILE_INVALID"); }
    }
}
