using MiniDb;
namespace MiniDb.Tests;
public class StorageTests
{
    [Fact] public void ReopenAfterUpdateDelete() { string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db"); try { using(var db=EngineTests.Seed(path)) { db.Execute("UPDATE users SET name='Юлия' WHERE id=1"); db.Execute("DELETE FROM users WHERE id=2"); } Assert.Equal(0,new FileInfo(path).Length%4096); using var reopened=new Database(path); Assert.Equal("Юлия",reopened.Execute("SELECT name FROM users WHERE id=1").Rows[0][0]); Assert.Equal(2,reopened.Execute("SELECT * FROM users").Rows.Count); } finally { File.Delete(path); } }
    [Fact] public void OversizedRowIsRejectedAtomically() { using var db=EngineTests.Seed(); Assert.Throws<SqlException>(()=>db.Execute("INSERT INTO users VALUES(4,'"+new string('я',4096)+"',2)")); Assert.Equal(3,db.Execute("SELECT * FROM users").Rows.Count); }
    [Fact] public void CorruptPageRejected() => Assert.Throws<SqlException>(()=>PageFormat.Decode(new byte[4096]).ToList());
}
