using MiniDb;
namespace MiniDb.Tests;
public class RegressionTests
{
    [Theory]
    [InlineData("age>=25 AND age<=31", 3)]
    [InlineData("age!=25", 1)]
    [InlineData("age<>25", 1)]
    [InlineData("age<31", 2)]
    [InlineData("(age=25 OR age=31) AND id>1", 2)]
    [InlineData("name='Анна'", 1)]
    public void Comparisons(string where, int count)
    {
        using var db = EngineTests.Seed();
        Assert.Equal(count, db.Execute("SELECT * FROM users WHERE " + where).Rows.Count);
    }
    [Fact]
    public void ReorderedInsertAndEscapedString()
    {
        using var db = EngineTests.Seed();
        db.Execute("INSERT INTO users(age,id,name) VALUES(50,4,'O''Brien;тест')");
        Assert.Equal("O'Brien;тест", db.Execute("SELECT name FROM users WHERE id=4").Rows[0][0]);
        Assert.Equal(2, SqlScript.Split("SELECT * FROM users WHERE name='O''Brien;тест'; SELECT * FROM users;").Count());
    }
    [Fact]
    public void DdlRollbackAndDispose()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "db");
        try
        {
            using (var db = EngineTests.Seed(path))
            {
                db.Execute("BEGIN");
                db.Execute("CREATE TABLE temporary(id INT)");
                db.Execute("CREATE INDEX ix ON users(age)");
                db.Execute("ROLLBACK");
                Assert.Throws<SqlException>(() => db.Execute("SELECT * FROM temporary"));
                Assert.Contains("Full scan", db.Execute("EXPLAIN SELECT * FROM users WHERE age=25").Rows[0][0].ToString());
                db.Execute("BEGIN");
                db.Execute("DELETE FROM users");
            }
            using var reopened = new Database(path);
            Assert.Equal(3, reopened.Execute("SELECT * FROM users").Rows.Count);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void IndexAndFullScanSameForAllKeysAfterChanges()
    {
        using var db = EngineTests.Seed();
        db.Execute("CREATE INDEX ix ON users(age)");
        db.Execute("INSERT INTO users VALUES(4,'Четыре',25),(5,'Пять',33)");
        db.Execute("UPDATE users SET age=33 WHERE id=1");
        db.Execute("DELETE FROM users WHERE id=2");
        for (int i = 20; i < 40; i++)
        {
            string sql = $"SELECT id FROM users WHERE age={i} ORDER BY id";
            db.ForceFullScan = true;
            var full = db.Execute(sql);
            db.ForceFullScan = false;
            var indexed = db.Execute(sql);
            Assert.Equal(full.Rows.Select(r => r[0]), indexed.Rows.Select(r => r[0]));
        }
    }
    [Fact]
    public void SchemaChecksEvenOnEmptyTables()
    {
        using var db = new Database();
        db.Execute("CREATE TABLE empty(id INT)");
        Assert.Throws<SqlException>(() => db.Execute("SELECT x FROM empty"));
        Assert.Throws<SqlException>(() => db.Execute("SELECT * FROM empty WHERE id='wrong'"));
        Assert.Throws<SqlException>(() => db.Execute("UPDATE empty SET id='wrong'"));
        Assert.Throws<SqlException>(() => db.Execute("DELETE FROM empty WHERE id='wrong'"));
        Assert.Throws<SqlException>(() => db.Execute("SELECT SUM(*) FROM empty"));
        Assert.Throws<SqlException>(() => db.Execute("SELECT COUNT(*),id FROM empty"));
    }
    [Fact]
    public void TwoWritersAreRejected()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "db");
        try
        {
            using var first = new Database(path);
            Assert.Throws<IOException>(() => new Database(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
