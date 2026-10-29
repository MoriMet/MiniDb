using System.Globalization;
namespace MiniDb;

public record Token(string Text, int Position, bool String = false);
public class SqlException(string message) : Exception(message);
public static class Lexer
{
    public static List<Token> Scan(string sql)
    {
        var result = new List<Token>();
        for (int i = 0; i < sql.Length;)
        {
            if (char.IsWhiteSpace(sql[i]))
            {
                i++;
                continue;
            }
            int start = i;
            if (sql[i] == '\'')
            {
                i++;
                var value = new System.Text.StringBuilder();
                bool closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        i++;
                        if (i < sql.Length && sql[i] == '\'')
                        {
                            value.Append('\'');
                            i++;
                        }
                        else
                        {
                            closed = true;
                            break;
                        }
                    }
                    else
                        value.Append(sql[i++]);
                }
                if (!closed)
                    throw new SqlException($"Незакрытая строка в позиции {start}");
                result.Add(new(value.ToString(), start, true));
                continue;
            }
            if (char.IsLetter(sql[i]) || sql[i] == '_')
            {
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                    i++;
            }
            else if (char.IsDigit(sql[i]) || (sql[i] == '-' && i + 1 < sql.Length && char.IsDigit(sql[i + 1])))
            {
                i++;
                while (i < sql.Length && char.IsDigit(sql[i]))
                    i++;
            }
            else if ("(),;.*=<>!".Contains(sql[i]))
            {
                i++;
                if (i < sql.Length && (sql[i] == '=' || (sql[start] == '<' && sql[i] == '>')))
                    i++;
            }
            else
                throw new SqlException($"Неожиданный символ в позиции {i}");
            result.Add(new(sql[start..i], start));
        }
        result.Add(new("<EOF>", sql.Length));
        return result;
    }
}
public abstract record Expression;
public record Literal(object Value) : Expression;
public record Column(string Name) : Expression;
public record Binary(Expression Left, string Operator, Expression Right) : Expression;
public record Field(string Name, string? Aggregate = null);
public record ColumnDefinition(string Name, string Type, bool PrimaryKey);
public abstract record Statement;
public record CreateTable(string Name, List<ColumnDefinition> Columns) : Statement;
public record Insert(string Table, List<string>? Columns, List<List<object>> Rows) : Statement;
public record Select(string Table, List<Field> Fields, string? Join, Expression? On, Expression? Where, List<string> Group, string? Order, bool Desc, int? Limit, bool Explain = false) : Statement;
public record Update(string Table, Dictionary<string, object> Values, Expression? Where) : Statement;
public record Delete(string Table, Expression? Where) : Statement;
public record CreateIndex(string Name, string Table, string Column) : Statement;
public record Transaction(string Command) : Statement;
public class Parser
{
    readonly List<Token> tokens; int index;
    Token Current => tokens[index];
    public Parser(string sql)
    {
        tokens = Lexer.Scan(sql);
    }
    bool Is(string text) => !Current.String && Current.Text.Equals(text, StringComparison.OrdinalIgnoreCase);
    bool Take(string text)
    {
        if (!Is(text))
            return false;
        index++;
        return true;
    }
    void Need(string text)
    {
        if (!Take(text))
            throw Error($"Ожидалось {text}");
    }
    SqlException Error(string text) => new($"{text} в позиции {Current.Position}");
    string Name()
    {
        if (Current.String || !(char.IsLetter(Current.Text[0]) || Current.Text[0] == '_'))
            throw Error("Ожидался идентификатор");
        return tokens[index++].Text;
    }
    string Reference()
    {
        string name = Name();
        if (Take("."))
            name += "." + Name();
        return name;
    }
    object Value()
    {
        if (Current.String)
            return tokens[index++].Text;
        if (long.TryParse(Current.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
        {
            index++;
            return v;
        }
        throw Error("Ожидалось INT или строка");
    }
    List<T> Comma<T>(Func<T> read)
    {
        var list = new List<T> { read() };
        while (Take(","))
            list.Add(read());
        return list;
    }
    Expression Operand()
    {
        if (Current.String || long.TryParse(Current.Text, out _))
            return new Literal(Value());
        return new Column(Reference());
    }
    Expression Atom()
    {
        if (Take("("))
        {
            var e = Or();
            Need(")");
            return e;
        }
        var left = Operand();
        string op = Current.Text;
        if (!new[] { "=", "!=", "<>", ">", "<", ">=", "<=" }.Contains(op))
            throw Error("Ожидался оператор сравнения");
        index++;
        return new Binary(left, op, Operand());
    }
    Expression And()
    {
        var e = Atom();
        while (Take("AND"))
            e = new Binary(e, "AND", Atom());
        return e;
    }
    Expression Or()
    {
        var e = And();
        while (Take("OR"))
            e = new Binary(e, "OR", And());
        return e;
    }
    Expression? Where() => Take("WHERE") ? Or() : null;
    public Statement Parse()
    {
        Statement result;
        if (Take("CREATE"))
        {
            if (Take("TABLE"))
            {
                string name = Name();
                Need("(");
                var cols = Comma(() => { string n = Name(); string type = Name().ToUpperInvariant(); if (type is not ("INT" or "TEXT")) throw Error("Допустимы INT и TEXT"); bool pk = Take("PRIMARY"); if (pk) Need("KEY"); return new ColumnDefinition(n, type, pk); });
                Need(")");
                result = new CreateTable(name, cols);
            }
            else
            {
                Need("INDEX");
                string name = Name();
                Need("ON");
                string table = Name();
                Need("(");
                string col = Name();
                Need(")");
                result = new CreateIndex(name, table, col);
            }
        }
        else if (Take("INSERT"))
        {
            Need("INTO");
            string table = Name();
            List<string>? cols = null;
            if (Take("("))
            {
                cols = Comma(Name);
                Need(")");
            }
            Need("VALUES");
            var rows = Comma(() => { Need("("); var row = Comma(Value); Need(")"); return row; });
            result = new Insert(table, cols, rows);
        }
        else if (Is("SELECT") || Is("EXPLAIN"))
        {
            bool explain = Take("EXPLAIN");
            Need("SELECT");
            var fields = Comma(() => { if (Take("*")) return new Field("*"); string n = Reference(); if (Take("(")) { string arg = Take("*") ? "*" : Reference(); Need(")"); if (!new[] { "COUNT", "SUM", "AVG" }.Contains(n.ToUpperInvariant())) throw Error("Неизвестный агрегат"); return new Field(arg, n.ToUpperInvariant()); } return new Field(n); });
            Need("FROM");
            string table = Name();
            string? join = null;
            Expression? on = null;
            if (Take("JOIN"))
            {
                join = Name();
                Need("ON");
                on = Atom();
            }
            var where = Where();
            var group = new List<string>();
            if (Take("GROUP"))
            {
                Need("BY");
                group = Comma(Reference);
            }
            string? order = null;
            bool desc = false;
            if (Take("ORDER"))
            {
                Need("BY");
                order = Reference();
                desc = Take("DESC");
                if (!desc)
                    Take("ASC");
            }
            int? limit = null;
            if (Take("LIMIT"))
            {
                var v = Value();
                if (v is not long l || l < 0 || l > int.MaxValue)
                    throw Error("Некорректный LIMIT");
                limit = (int)l;
            }
            result = new Select(table, fields, join, on, where, group, order, desc, limit, explain);
        }
        else if (Take("UPDATE"))
        {
            string table = Name();
            Need("SET");
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            do
            {
                string col = Name();
                Need("=");
                if (!values.TryAdd(col, Value()))
                    throw Error("Повтор столбца");
            } while (Take(","));
            result = new Update(table, values, Where());
        }
        else if (Take("DELETE"))
        {
            Need("FROM");
            string table = Name();
            result = new Delete(table, Where());
        }
        else if (Is("BEGIN") || Is("COMMIT") || Is("ROLLBACK"))
            result = new Transaction(tokens[index++].Text.ToUpperInvariant());
        else
            throw Error("Неизвестная команда");
        Take(";");
        Need("<EOF>");
        return result;
    }
}
