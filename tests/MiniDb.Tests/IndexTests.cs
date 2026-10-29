using MiniDb;
namespace MiniDb.Tests;
public class IndexTests
{
    [Fact]
    public void SplitsDuplicatesAndPersistence()
    {
        var tree = new BTree();
        foreach (int i in Enumerable.Range(0, 2000).Reverse())
        {
            tree.Add((long)i, i);
            tree.Add((long)i, i + 2000);
        }
        Assert.True(tree.Height >= 3);
        tree = BTree.Deserialize(tree.Serialize());
        for (int i = 0; i < 2000; i++)
            Assert.Equal(new long[] { i, i + 2000 }, tree.Find((long)i));
        Assert.Empty(tree.Find(9999L));
    }
    [Fact]
    public void RealSeekAndMutation()
    {
        using var db = EngineTests.Seed();
        db.Execute("CREATE INDEX age_idx ON users(age)");
        Assert.Contains("Index seek", (string)db.Execute("EXPLAIN SELECT * FROM users WHERE age=25").Rows[0][0]);
        Assert.Equal(2, db.Execute("SELECT * FROM users WHERE age=25").Rows.Count);
        Assert.Equal(2, db.LastRowsExamined);
        db.Execute("UPDATE users SET age=40 WHERE id=1");
        Assert.Single(db.Execute("SELECT * FROM users WHERE age=25").Rows);
        db.Execute("DELETE FROM users WHERE age=25");
        Assert.Empty(db.Execute("SELECT * FROM users WHERE age=25").Rows);
        Assert.Contains("Full scan", (string)db.Execute("EXPLAIN SELECT * FROM users WHERE age=40 OR id=1").Rows[0][0]);
    }
    [Fact]
    public void IndexSurvivesRestart()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            using (var db = EngineTests.Seed(path))
                db.Execute("CREATE INDEX age_idx ON users(age)");
            using var db2 = new Database(path);
            Assert.Equal(2, db2.Execute("SELECT * FROM users WHERE age=25").Rows.Count);
            Assert.Equal(2, db2.LastRowsExamined);
        }
        finally { File.Delete(path); }
    }
}
