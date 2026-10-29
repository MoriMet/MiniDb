using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MiniDb;
BenchmarkRunner.Run<SearchBenchmark>();
[MemoryDiagnoser]
[ShortRunJob]
public class SearchBenchmark
{
    Database db = null!;
    Statement query = null!;
    [GlobalSetup]
    public void Setup()
    {
        db = new Database();
        DemoData.Generate(db);
        db.Execute("CREATE INDEX idx_age ON users(age)");
        query = new Parser("SELECT * FROM users WHERE age=54321").Parse();
        var scan = Scan();
        var index = Index();
        if (scan.Rows.Count != 1 || index.Rows.Count != 1 || !scan.Rows[0].SequenceEqual(index.Rows[0]))
            throw new Exception("Search results differ");
    }
    [Benchmark(Baseline = true)]
    public QueryResult Scan()
    {
        db.ForceFullScan = true;
        return db.Execute(query);
    }
    [Benchmark]
    public QueryResult Index()
    {
        db.ForceFullScan = false;
        return db.Execute(query);
    }
    [GlobalCleanup] public void Cleanup() => db.Dispose();
}
