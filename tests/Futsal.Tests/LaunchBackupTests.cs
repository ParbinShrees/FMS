using System.Text;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Tests;
public sealed class LaunchBackupTests(Milestone8PostgresFixture fixture):IClassFixture<Milestone8PostgresFixture>
{
 [Fact]public async Task Encrypted_backup_restore_roundtrip_matches_real_database()
 {
  var root=FindRoot();var container=fixture.Container;
  var install=await container.ExecAsync(["sh","-c","apk add --no-cache age bash findutils coreutils >/dev/null"]);Assert.Equal(0,install.ExitCode);
  var mkdir=await container.ExecAsync(["mkdir","-p","/tmp/deploy","/tmp/backups"]);Assert.Equal(0,mkdir.ExitCode);
  foreach(var name in new[]{"backup.sh","restore.sh","verify-restore.sql","backup-restore-check.sh"})await container.CopyAsync(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,"deploy",name)).Replace("\r\n","\n")),"/tmp/deploy/"+name);
  // Source rows prove this isn't only an empty-database round trip. No test modifies development data.
  await using(var db=new FutsalDbContext(fixture.Options,new SuperAdminUser(),new FixedClock(DateTimeOffset.UtcNow))){db.Organizations.Add(new(){Name="Restore drill",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();}
  var cs=new Npgsql.NpgsqlConnectionStringBuilder(container.GetConnectionString());
  // Credentials stay in an ephemeral container file and never in test output.
  var env=$"export PGHOST=127.0.0.1 PGUSER='{cs.Username}' PGDATABASE='{cs.Database}' PGPASSWORD='{cs.Password}' BACKUP_DIR=/tmp/backups\n";
  await container.CopyAsync(Encoding.UTF8.GetBytes(env+"age-keygen -o /tmp/backup-identity 2>/dev/null\nexport BACKUP_RECIPIENT=$(age-keygen -y /tmp/backup-identity)\nexport BACKUP_IDENTITY=$(cat /tmp/backup-identity)\nbash /tmp/deploy/backup-restore-check.sh\n"),"/tmp/drill.sh");
  var result=await container.ExecAsync(["bash","/tmp/drill.sh"]);Assert.True(result.ExitCode==0,"Backup/restore failed: "+result.Stderr);Assert.Contains("row counts and signed payment totals match",result.Stdout);
 }
 private static string FindRoot(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d!=null&&!File.Exists(Path.Combine(d.FullName,"AGENTS.md")))d=d.Parent;return d?.FullName??throw new InvalidOperationException("Repository root not found.");}
}
