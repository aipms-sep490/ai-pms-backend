# Hướng dẫn setup AI-PMS Backend cho member

Tài liệu này dành cho thành viên backend chạy dự án trên máy local. Không gửi hoặc commit `appsettings.json`, chuỗi kết nối database, JWT signing key và mật khẩu tài khoản.

## 1. Yêu cầu

- .NET 8 SDK.
- Git.
- Kết nối được tới SQL Server dùng chung của team.
- Docker chỉ cần thiết nếu member muốn tự chạy SQL Server hoặc Redis local.

Kiểm tra SDK:

```powershell
dotnet --version
```

## 2. Lấy source code và tạo cấu hình local

Sau khi clone repository và chuyển vào thư mục `ai-pms-backend`, chạy:

```powershell
Copy-Item src/AIPMS.Api/appsettings.example.json src/AIPMS.Api/appsettings.json
dotnet restore AIPMS.sln
dotnet tool restore
```

Trên WSL/Linux:

```bash
cp src/AIPMS.Api/appsettings.example.json src/AIPMS.Api/appsettings.json
dotnet restore AIPMS.sln
dotnet tool restore
```

`appsettings.json` được Git bỏ qua và chỉ dùng trên máy của member.

## 3. Cấu hình database

Leader gửi chuỗi kết nối SQL Server qua kênh riêng. Member lưu chuỗi kết nối bằng User Secrets, không dán trực tiếp vào file được commit:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "CHUOI_KET_NOI_DO_LEADER_CUNG_CAP" --project src/AIPMS.Api
```

Không chia sẻ chuỗi kết nối trong issue, pull request, ảnh chụp màn hình hoặc nhóm chat công khai.

## 4. Tạo JWT key local

Mỗi member backend tự tạo một JWT signing key dùng trên máy của mình. Không cần xin key local của leader.

PowerShell:

```powershell
$jwtKey = [guid]::NewGuid().ToString("N")
dotnet user-secrets set "Jwt:SigningKey" $jwtKey --project src/AIPMS.Api
```

WSL/Linux:

```bash
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -hex 32)" --project src/AIPMS.Api
```

Các giá trị không bí mật như issuer, audience và thời gian sống của access token đã có trong `appsettings.example.json`.

Có thể kiểm tra tên các cấu hình đã lưu bằng lệnh dưới đây. Không chụp hoặc gửi kết quả vì lệnh sẽ hiển thị giá trị secret:

```powershell
dotnet user-secrets list --project src/AIPMS.Api
```

## 5. Build, test và chạy API

```powershell
dotnet build AIPMS.sln
dotnet test AIPMS.sln --no-build
dotnet run --project src/AIPMS.Api
```

Swagger mặc định:

```text
http://localhost:5080/swagger
```

Các endpoint kiểm tra ban đầu:

```text
POST /api/v1/auth/login
GET  /api/v1/auth/me
GET  /api/v1/system
```

Sau khi login, sao chép `accessToken` và nhập vào nút **Authorize** của Swagger theo định dạng:

```text
Bearer ACCESS_TOKEN
```

## 6. Cấu hình khi chạy bằng Docker hoặc trên server

User Secrets chỉ dành cho local development. Container và server sử dụng environment variables:

```text
ConnectionStrings__DefaultConnection
Jwt__Issuer
Jwt__Audience
Jwt__SigningKey
Jwt__AccessTokenMinutes
```

Không đặt giá trị production thật trong `docker-compose.yml` hoặc file được Git theo dõi. Đặt chúng trong secret store của server hoặc file `.env` đã được Git bỏ qua.

## 7. Checklist trước khi tạo pull request

- `dotnet build AIPMS.sln` thành công và không có warning mới.
- `dotnet test AIPMS.sln --no-build` chạy thành công.
- Không có `appsettings.json`, `.env`, chuỗi kết nối, mật khẩu hoặc JWT key trong thay đổi Git.
- Route mới dùng prefix `/api/v1/`.
- Endpoint mới có validation, authorization và test phù hợp.

Nếu thiếu chuỗi kết nối hoặc tài khoản test, liên hệ Leader BE qua kênh riêng. JWT key local do từng member tự tạo.
