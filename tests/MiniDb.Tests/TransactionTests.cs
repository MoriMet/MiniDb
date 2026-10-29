using MiniDb;
using System.Diagnostics;
namespace MiniDb.Tests;
public class TransactionTests
{
    [Fact] public void CommitRollbackAndFailedStatement() { using var db=EngineTests.Seed(); db.Execute("CREATE INDEX ix ON users(age)"); db.Execute("BEGIN"); db.Execute("UPDATE users SET age=40 WHERE id=1"); Assert.Throws<SqlException>(()=>db.Execute("UPDATE users SET id=2")); Assert.Single(db.Execute("SELECT * FROM users WHERE age=40").Rows); db.Execute("ROLLBACK"); Assert.Empty(db.Execute("SELECT * FROM users WHERE age=40").Rows); db.Execute("BEGIN"); db.Execute("UPDATE users SET age=40 WHERE id=1"); db.Execute("COMMIT"); Assert.Single(db.Execute("SELECT * FROM users WHERE age=40").Rows); Assert.Throws<SqlException>(()=>db.Execute("COMMIT")); }
    [Theory] [InlineData("uncommitted",false)] [InlineData("before-commit",false)] [InlineData("after-commit",true)] [InlineData("checkpoint",true)]
    public void KillRealProcessAndRecover(string point,bool committed)
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(dir); string path=Path.Combine(dir,"crash.db"),signal=Path.Combine(dir,"signal");
        try
        {
            using(var db=EngineTests.Seed(path)) db.Execute("CREATE INDEX ix ON users(age)");
            string assembly=typeof(Database).Assembly.Location;
            // Use the console project's own runtimeconfig/deps, not the testhost entry point.
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../")); string config= new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string executable=Path.Combine(root,"src","MiniDb","bin",config,"net8.0","MiniDb.dll");
            var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet") { UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true };
            foreach(string arg in new[]{executable,"--db",path,"--crash-worker","--point",point,"--signal",signal}) info.ArgumentList.Add(arg);
            using var process=Process.Start(info)!;
            try { Assert.True(SpinWait.SpinUntil(()=>File.Exists(signal)||process.HasExited,TimeSpan.FromSeconds(20)),"Worker timeout"); Assert.False(process.HasExited,process.HasExited?process.StandardError.ReadToEnd():""); process.Kill(true); Assert.True(process.WaitForExit(10000)); }
            finally { if(!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
            // Append a torn tail: both recovery and a second recovery must be idempotent.
            using(var tail=new FileStream(path+".wal",FileMode.Append)) tail.Write(new byte[]{1,2,3,4,5});
            for(int i=0;i<2;i++) { using var recovered=new Database(path); Assert.Equal(committed?2:0,recovered.Execute("SELECT * FROM users WHERE age=99").Rows.Count); Assert.Equal(committed?4:3,recovered.Execute("SELECT * FROM users").Rows.Count); Assert.Equal(committed?1:2,recovered.Execute("SELECT * FROM users WHERE age=25").Rows.Count); }
        }
        finally { Directory.Delete(dir,true); }
    }
}
