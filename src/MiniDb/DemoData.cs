using System.Diagnostics;
namespace MiniDb;
public static class DemoData
{
    public static void Generate(Database db, int count = 100000)
    {
        if (count < 1 || count > 1000000)
            throw new SqlException("Генератор: от 1 до 1000000 строк");
        db.Execute("BEGIN");
        try
        {
            db.Execute("CREATE TABLE users(id INT PRIMARY KEY,name TEXT,age INT)");
            var rows = new List<List<object>>(count);
            for (int i = 1; i <= count; i++)
                rows.Add(new() { (long)i, "Пользователь " + i, (long)i });
            db.Execute(new Insert("users", null, rows));
            db.Execute("COMMIT");
        }
        catch { db.Execute("ROLLBACK"); throw; }
    }
    public static (double Scan, double Index) Measure(Database db)
    {
        var query = new Parser("SELECT * FROM users WHERE age = 54321").Parse();
        db.ForceFullScan = false;
        string plan = db.Execute("EXPLAIN SELECT * FROM users WHERE age=54321").Rows[0][0].ToString()!;
        if (!plan.StartsWith("Index seek", StringComparison.Ordinal))
            throw new SqlException("Для --measure сначала создайте CREATE INDEX idx_age ON users(age)");
        db.ForceFullScan = true;
        var full = db.Execute(query);
        db.ForceFullScan = false;
        var indexed = db.Execute(query);
        if (full.Rows.Count != 1 || indexed.Rows.Count != 1 || !full.Rows[0].SequenceEqual(indexed.Rows[0]))
            throw new InvalidOperationException("Результаты поиска не совпали");
        double Time(bool scan, int iterations)
        {
            db.ForceFullScan = scan;
            for (int i = 0; i < 3; i++)
                db.Execute(query);
            var samples = new List<double>();
            for (int r = 0; r < 7; r++)
            {
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                    db.Execute(query);
                clock.Stop();
                samples.Add(clock.Elapsed.TotalMilliseconds / iterations);
            }
            return samples.Order().ElementAt(3);
        }
        double a = Time(true, 5), b = Time(false, 1000);
        db.ForceFullScan = false;
        return (a, b);
    }
}
