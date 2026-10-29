using MiniDb;
namespace MiniDb.Tests;
public class EngineTests
{
    internal static Database Seed(string? path = null)
    {
        var db = new Database(path);
        db.Execute("CREATE TABLE users(id INT PRIMARY KEY,name TEXT,age INT)");
        db.Execute("INSERT INTO users VALUES(1,'Анна',25),(2,'Иван',31),(3,'Олег',25)");
        return db;
    }
    [Fact]
    public void CrudAndFilters()
    {
        using var db = Seed();
        Assert.Equal("Анна", db.Execute("SELECT name FROM users WHERE age>20 ORDER BY name LIMIT 1").Rows[0][0]);
        Assert.Equal(2, db.Execute("SELECT * FROM users WHERE age=25 OR age=31 AND id=99").Rows.Count);
        db.Execute("UPDATE users SET age=40 WHERE id=1");
        db.Execute("DELETE FROM users WHERE age<30");
        Assert.Equal(2, db.Execute("SELECT * FROM users").Rows.Count);
    }
    [Fact]
    public void ConstraintsAreAtomic()
    {
        using var db = Seed();
        Assert.Throws<SqlException>(() => db.Execute("INSERT INTO users VALUES(4,'a',5),(1,'b',6)"));
        Assert.Equal(3, db.Execute("SELECT * FROM users").Rows.Count);
        Assert.Throws<SqlException>(() => db.Execute("UPDATE users SET age='bad'"));
        Assert.Throws<SqlException>(() => db.Execute("SELECT x FROM users"));
        Assert.Throws<SqlException>(() => db.Execute("SELECT * FROM missing"));
        Assert.Throws<SqlException>(() => db.Execute("INSERT INTO users VALUES(4,'a')"));
    }
    [Fact]
    public void Aggregates()
    {
        using var db = Seed();
        var r = db.Execute("SELECT age,COUNT(*),SUM(id),AVG(id) FROM users GROUP BY age ORDER BY age");
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(2L, r.Rows[0][1]);
        Assert.Equal(4m, r.Rows[0][2]);
        Assert.Equal(2m, r.Rows[0][3]);
        db.Execute("DELETE FROM users");
        Assert.Equal(0L, db.Execute("SELECT COUNT(*) FROM users").Rows[0][0]);
    }
}
