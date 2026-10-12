using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests.Meetings;

public sealed class MeetingCollaborationMigrationTests
{
    [Fact]
    public async Task Legacy_rsvp_backfill_does_not_infer_attendance_and_rerun_preserves_response()
    {
        await using var db = new IsolatedSqlDatabase();
        await db.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"), bootstrap: false);
        await SqlSchemaBootstrap.ExecuteAsync(db.ConnectionString, """
            CREATE TABLE dbo.users(id bigint PRIMARY KEY);
            CREATE TABLE dbo.meetings(id bigint PRIMARY KEY);
            CREATE TABLE dbo.meeting_participants(id bigint PRIMARY KEY,attendance_status varchar(16));
            INSERT dbo.users VALUES(1); INSERT dbo.meetings VALUES(1);
            INSERT dbo.meeting_participants VALUES(1,'ACCEPTED'),(2,'DECLINED'),(3,'ATTENDED'),(4,'ABSENT'),(5,NULL);
            """);
        var sql = await File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "changes", "20261012_add_meeting_collaboration.sql"));
        await SqlSchemaBootstrap.ExecuteAsync(db.ConnectionString, sql);
        await using var connection = new SqlConnection(db.ConnectionString); await connection.OpenAsync();
        async Task<object?> Scalar(string query) { await using var command = new SqlCommand(query, connection); return await command.ExecuteScalarAsync(); }
        Assert.Equal(1, await Scalar("SELECT COUNT(*) FROM dbo.meeting_participants WHERE id=1 AND rsvp_status='ACCEPTED'"));
        Assert.Equal(1, await Scalar("SELECT COUNT(*) FROM dbo.meeting_participants WHERE id=2 AND rsvp_status='DECLINED'"));
        Assert.Equal(3, await Scalar("SELECT COUNT(*) FROM dbo.meeting_participants WHERE id>=3 AND rsvp_status='INVITED'"));
        Assert.Equal(5, await Scalar("SELECT COUNT(DISTINCT rsvp_token) FROM dbo.meeting_participants"));
        await SqlSchemaBootstrap.ExecuteAsync(db.ConnectionString, "UPDATE dbo.meeting_participants SET rsvp_status='TENTATIVE' WHERE id=1;");
        await SqlSchemaBootstrap.ExecuteAsync(db.ConnectionString, sql);
        Assert.Equal("TENTATIVE", await Scalar("SELECT rsvp_status FROM dbo.meeting_participants WHERE id=1"));
        Assert.Equal("ACCEPTED", await Scalar("SELECT attendance_status FROM dbo.meeting_participants WHERE id=1"));
        await Assert.ThrowsAsync<SqlException>(() => SqlSchemaBootstrap.ExecuteAsync(db.ConnectionString,
            "UPDATE dbo.meetings SET series_id=NEWID(),occurrence_number=NULL WHERE id=1;"));
    }
}
