namespace MiniDb;
public class Table(string name, List<ColumnDefinition> columns)
{
    public string Name { get; } = name;
    public List<ColumnDefinition> Columns { get; } = columns;
    public Dictionary<long, object[]> Rows { get; } = new();
    public long NextId { get; set; } = 1;
    public int ColumnIndex(string name) { int i = Columns.FindIndex(c => c.Name.Equals(name,StringComparison.OrdinalIgnoreCase)); if (i < 0) throw new SqlException($"Неизвестный столбец {name}"); return i; }
    public Table Clone() { var t = new Table(Name,Columns.ToList()) { NextId = NextId }; foreach (var r in Rows) t.Rows.Add(r.Key,r.Value.ToArray()); return t; }
}
public record QueryResult(string[] Columns, List<object[]> Rows, string Message = "");
public class Database : IDisposable
{
    internal Dictionary<string,Table> Tables = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string,Table>? backup;
    public Database(string? path = null) { }
    public QueryResult Execute(string sql) => Execute(new Parser(sql).Parse());
    public QueryResult Execute(Statement statement)
    {
        if (statement is Transaction tx)
        {
            if (tx.Command == "BEGIN") { if (backup != null) throw new SqlException("Транзакция уже начата"); backup = Snapshot(); }
            else { if (backup == null) throw new SqlException("Нет активной транзакции"); if (tx.Command == "ROLLBACK") Tables = backup; backup = null; }
            return new([],[],tx.Command);
        }
        if (statement is Select select) return SelectRows(select);
        var before = Snapshot();
        try { int changed = Mutate(statement); return new([],[],$"OK: {changed}"); }
        catch { Tables = before; throw; }
    }
    Dictionary<string,Table> Snapshot() => Tables.ToDictionary(p => p.Key,p => p.Value.Clone(),StringComparer.OrdinalIgnoreCase);
    Table Get(string name) => Tables.TryGetValue(name,out var t) ? t : throw new SqlException($"Неизвестная таблица {name}");
    static void CheckType(ColumnDefinition col, object value) { if ((col.Type == "INT" && value is not long) || (col.Type == "TEXT" && value is not string)) throw new SqlException($"Неверный тип {col.Name}: ожидается {col.Type}"); }
    static void Validate(Table t)
    {
        if (t.Columns.Count(c=>c.PrimaryKey)>1) throw new SqlException("Поддерживается один PRIMARY KEY");
        var unique = new HashSet<object>(); int pk = t.Columns.FindIndex(c=>c.PrimaryKey);
        foreach (var row in t.Rows.Values) { for (int i=0;i<row.Length;i++) CheckType(t.Columns[i],row[i]); if (pk>=0 && !unique.Add(row[pk])) throw new SqlException("PRIMARY KEY должен быть уникальным"); }
    }
    int Mutate(Statement s)
    {
        switch(s)
        {
            case CreateTable c:
                if (c.Columns.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=c.Columns.Count) throw new SqlException("Повтор имени столбца");
                var fresh = new Table(c.Name,c.Columns); Validate(fresh); if (!Tables.TryAdd(c.Name,fresh)) throw new SqlException("Таблица уже существует"); return 0;
            case Insert ins:
                var t = Get(ins.Table); var positions = ins.Columns?.Select(t.ColumnIndex).ToArray() ?? Enumerable.Range(0,t.Columns.Count).ToArray();
                if (positions.Distinct().Count()!=positions.Length || positions.Length!=t.Columns.Count) throw new SqlException("Нужно указать все столбцы ровно один раз");
                foreach(var values in ins.Rows) { if(values.Count!=positions.Length) throw new SqlException("Неверное количество значений"); var row = new object[positions.Length]; for(int i=0;i<row.Length;i++) row[positions[i]]=values[i]; t.Rows.Add(t.NextId++,row); } Validate(t); return ins.Rows.Count;
            case Update u:
                var ut=Get(u.Table); var setters=u.Values.Select(p=>(Index:ut.ColumnIndex(p.Key),p.Value)).ToList(); var uh=Headers(ut); Bind(u.Where,uh);
                var targets=ut.Rows.Values.Where(r=>Matches(u.Where,uh,r)).ToList(); foreach(var r in targets) foreach(var p in setters) r[p.Index]=p.Value; Validate(ut); return targets.Count;
            case Delete d:
                var dt=Get(d.Table); var dh=Headers(dt); Bind(d.Where,dh); var ids=dt.Rows.Where(p=>Matches(d.Where,dh,p.Value)).Select(p=>p.Key).ToList(); foreach(var id in ids) dt.Rows.Remove(id); return ids.Count;
            default: throw new SqlException("Команда пока не реализована");
        }
    }
    static string[] Headers(Table t) => t.Columns.Select(c=>t.Name+"."+c.Name).ToArray();
    static int Resolve(string name,string[] headers)
    {
        var matches=Enumerable.Range(0,headers.Length).Where(i=>headers[i].Equals(name,StringComparison.OrdinalIgnoreCase) || (!name.Contains('.') && headers[i][(headers[i].IndexOf('.')+1)..].Equals(name,StringComparison.OrdinalIgnoreCase))).ToArray();
        if(matches.Length!=1) throw new SqlException(matches.Length==0 ? $"Неизвестный столбец {name}" : $"Неоднозначный столбец {name}"); return matches[0];
    }
    static void Bind(Expression? e,string[] h) { if(e is Column c) Resolve(c.Name,h); if(e is Binary b) { Bind(b.Left,h); Bind(b.Right,h); } }
    static object Eval(Expression e,string[] h,object[] r) => e switch { Literal l=>l.Value, Column c=>r[Resolve(c.Name,h)], Binary b when b.Operator=="AND" => (bool)Eval(b.Left,h,r)&&(bool)Eval(b.Right,h,r), Binary b when b.Operator=="OR" => (bool)Eval(b.Left,h,r)||(bool)Eval(b.Right,h,r), Binary b=>Compare(Eval(b.Left,h,r),b.Operator,Eval(b.Right,h,r)), _=>throw new SqlException("Выражение не поддерживается") };
    static bool Compare(object a,string op,object b) { if(a.GetType()!=b.GetType()) throw new SqlException("Нельзя сравнивать INT и TEXT"); int c=ValueComparer.Instance.Compare(a,b); return op switch { "="=>c==0,"!=" or "<>"=>c!=0,">"=>c>0,"<"=>c<0,">="=>c>=0,"<="=>c<=0,_=>false }; }
    static bool Matches(Expression? e,string[] h,object[] r)=>e==null || (bool)Eval(e,h,r);
    QueryResult SelectRows(Select q)
    {
        var t=Get(q.Table); string[] h=Headers(t); IEnumerable<object[]> rows=t.Rows.Values;
        if(q.Join!=null) throw new SqlException("JOIN пока не реализован");
        Bind(q.Where,h); foreach(var f in q.Fields) if(f.Name!="*") Resolve(f.Name,h);
        var groupPositions=q.Group.Select(n=>Resolve(n,h)).ToArray();
        bool aggregated=q.Fields.Any(f=>f.Aggregate!=null) || q.Group.Count>0;
        if(aggregated && q.Fields.Any(f=>f.Aggregate==null && !q.Group.Contains(f.Name,StringComparer.OrdinalIgnoreCase))) throw new SqlException("Столбец должен входить в GROUP BY");
        if(q.Explain) return new(["Plan"],[new object[]{"Full scan"}]);
        rows=rows.Where(r=>Matches(q.Where,h,r));
        var fields=q.Fields.Count==1 && q.Fields[0]==new Field("*") ? t.Columns.Select(c=>new Field(c.Name)).ToList() : q.Fields;
        if(fields.Any(f=>f.Name=="*"&&f.Aggregate==null)) throw new SqlException("* должен быть единственным полем");
        string[] output=fields.Select(f=>f.Aggregate==null?f.Name:$"{f.Aggregate}({f.Name})").ToArray();
        var result=new List<object[]>();
        if(aggregated)
        {
            var groups=rows.GroupBy(r=>new RowKey(groupPositions.Select(i=>r[i]).ToArray())).Select(g=>g.ToList()).ToList();
            if(groupPositions.Length==0 && groups.Count==0) groups.Add([]);
            foreach(var g in groups) result.Add(fields.Select(f=> { if(f.Aggregate==null) return g[0][Resolve(f.Name,h)]; if(f.Aggregate=="COUNT") return (object)(long)g.Count; int pos=Resolve(f.Name,h); if(t.Columns[pos].Type!="INT") throw new SqlException("SUM/AVG требуют INT"); decimal sum=g.Sum(r=>(decimal)(long)r[pos]); return f.Aggregate=="SUM" ? (object)sum : g.Count==0 ? (object)"NULL" : sum/g.Count; }).ToArray());
            if(q.Order!=null) { int pos=Array.FindIndex(output,n=>n.Equals(q.Order,StringComparison.OrdinalIgnoreCase)); if(pos<0) throw new SqlException("ORDER BY должен входить в результат"); result=(q.Desc?result.OrderByDescending(r=>r[pos],ValueComparer.Instance):result.OrderBy(r=>r[pos],ValueComparer.Instance)).ToList(); }
        }
        else
        {
            if(q.Order!=null) { int pos=Resolve(q.Order,h); rows=q.Desc?rows.OrderByDescending(r=>r[pos],ValueComparer.Instance):rows.OrderBy(r=>r[pos],ValueComparer.Instance); }
            if(q.Limit!=null) rows=rows.Take(q.Limit.Value);
            var positions=fields.Select(f=>Resolve(f.Name,h)).ToArray(); result=rows.Select(r=>positions.Select(i=>r[i]).ToArray()).ToList();
        }
        if(q.Limit!=null) result=result.Take(q.Limit.Value).ToList(); return new(output,result,$"Строк: {result.Count}");
    }
    public void Dispose() { }
}
public class ValueComparer : IComparer<object>
{
    public static readonly ValueComparer Instance=new();
    public int Compare(object? x,object? y) => x is string a && y is string b ? StringComparer.Ordinal.Compare(a,b) : ((IComparable)x!).CompareTo(y);
}
public sealed class RowKey(object[] values) : IEquatable<RowKey>
{
    public bool Equals(RowKey? other)=>other!=null && valuesCopy.SequenceEqual(other.valuesCopy);
    readonly object[] valuesCopy=values;
    public override bool Equals(object? obj)=>obj is RowKey k&&Equals(k);
    public override int GetHashCode() { var h=new HashCode(); foreach(var v in valuesCopy) h.Add(v); return h.ToHashCode(); }
}
