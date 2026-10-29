using MiniDb;
namespace MiniDb.Tests;
public class JoinTests
{
    [Fact]
    public void NestedLoopQualifiedAndAmbiguity()
    {
        using var db = EngineTests.Seed();
        db.Execute("CREATE TABLE orders(id INT PRIMARY KEY,user_id INT,amount INT)");
        db.Execute("INSERT INTO orders VALUES(10,1,100),(11,1,200),(12,2,50)");
        var r = db.Execute("SELECT users.name,orders.amount FROM users JOIN orders ON users.id=orders.user_id WHERE orders.amount>=100 ORDER BY orders.amount DESC LIMIT 1");
        Assert.Equal("Анна", r.Rows[0][0]);
        Assert.Equal(200L, r.Rows[0][1]);
        Assert.Equal(6, db.Execute("SELECT * FROM users JOIN orders ON users.id=orders.user_id").Columns.Length);
        Assert.Throws<SqlException>(() => db.Execute("SELECT id FROM users JOIN orders ON users.id=orders.user_id"));
        Assert.Throws<SqlException>(() => db.Execute("SELECT * FROM users JOIN orders ON users.id>orders.user_id"));
        Assert.Equal(300m, db.Execute("SELECT users.name,SUM(orders.amount) FROM users JOIN orders ON users.id=orders.user_id GROUP BY users.name ORDER BY users.name").Rows[0][1]);
    }
}
