using MiniDb;
namespace MiniDb.Tests;
public class SqlTests
{
    [Fact]
    public void LexerEscapesAndPositions()
    {
        var t = Lexer.Scan(" 'О''Б' >= 12");
        Assert.Equal("О'Б", t[0].Text);
        Assert.Equal(1, t[0].Position);
        Assert.Equal(">=", t[1].Text);
    }
    [Fact]
    public void Precedence()
    {
        var q = (Select)new Parser("select * from t where a=1 or b=2 and c=3").Parse();
        var b = Assert.IsType<Binary>(q.Where);
        Assert.Equal("OR", b.Operator);
        Assert.Equal("AND", Assert.IsType<Binary>(b.Right).Operator);
    }
    [Fact]
    public void Parentheses()
    {
        var q = (Select)new Parser("select * from t where (a=1 or b=2) and c=3").Parse();
        Assert.Equal("AND", Assert.IsType<Binary>(q.Where).Operator);
    }
    [Fact]
    public void ErrorHasPosition()
    {
        Assert.Contains("Ожидалось FROM в позиции 14", Assert.Throws<SqlException>(() => new Parser("SELECT name   users").Parse()).Message);
    }
    [Theory][InlineData("INSERT INTO t VALUES (1,'a'),(2,'b');", typeof(Insert))][InlineData("CREATE TABLE t(id INT PRIMARY KEY, name TEXT)", typeof(CreateTable))][InlineData("UPDATE t SET name='b' WHERE id=1", typeof(Update))][InlineData("DELETE FROM t WHERE id<>1", typeof(Delete))] public void Statements(string sql, Type type) => Assert.IsType(type, new Parser(sql).Parse());
}

public class InvalidSqlTests
{
    [Theory]
    [InlineData("SELECT * FROM t WHERE x='abc", "Незакрытая строка")]
    [InlineData("SELECT @ FROM t", "Неожиданный символ")]
    [InlineData("SELECT * FROM t LIMIT -1", "Некорректный LIMIT")]
    [InlineData("SELECT * FROM t; SELECT * FROM t", "Ожидалось <EOF>")]
    public void ErrorsAreUnderstandable(string sql, string expected)
    {
        string message = Assert.Throws<SqlException>(() => new Parser(sql).Parse()).Message;
        Assert.Contains(expected, message);
        Assert.Contains("позиции", message);
    }
}
